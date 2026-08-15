# 22단계 — 플레이어 인벤토리와 보관함

## 단계 목표

보드가 가득 차기 전에 중요한 아이템을 보드 밖에 보관하고 다시 꺼낼 수 있는 플레이어별
인벤토리를 추가했습니다. 보드와 인벤토리 revision을 함께 검증하고 아이템 ID를 유지한 채
두 저장소를 하나의 트랜잭션으로 변경합니다.

## 기본 규칙

- 새 인벤토리 기본 용량: 20칸
- 보관 아이템은 기존 `itemId`, chainId, level 유지
- 보관 시 보드에서 아이템 제거
- 복원 시 서버가 가장 낮은 빈 보드 슬롯 선택
- 인벤토리 안에서는 보드 머지나 판매 불가
- 부트스트랩에서 이전 계정의 인벤토리를 지연 생성

인벤토리 용량이 콘텐츠 카탈로그에 추가되어 버전을 `2026.08.15.3`으로 올렸습니다.

## API

조회:

```http
GET /api/v1/inventory/
Authorization: Bearer {accessToken}
```

보드에서 보관:

```http
POST /api/v1/inventory/store
```

```json
{
  "itemId": "...",
  "expectedBoardRevision": 4,
  "expectedInventoryRevision": 1,
  "idempotencyKey": "device-session-42:inventory-store:000001"
}
```

인벤토리에서 복원:

```http
POST /api/v1/inventory/items/{itemId}/restore
```

```json
{
  "expectedBoardRevision": 5,
  "expectedInventoryRevision": 2,
  "idempotencyKey": "device-session-42:inventory-restore:000001"
}
```

성공 응답에는 실제 `action`, 전체 `board`, 전체 `inventory`, itemId, 복원 시
`targetSlot`, `replayed`가 포함됩니다.

## 원자성과 멱등성

보관 트랜잭션:

1. 보드에서 아이템 제거와 board revision 증가
2. 같은 ID의 인벤토리 아이템 추가와 inventory revision 증가
3. 멱등 영수증 저장

복원 트랜잭션은 반대로 인벤토리 아이템을 제거하고 같은 ID의 보드 아이템을 서버 선택
슬롯에 추가합니다. 어느 저장도 일부만 성공할 수 없습니다.

`inventory_transfer_receipts`는 `(player_id, idempotency_key)` 유니크 키와 최초 응답을
저장합니다. 같은 키 재시도는 추가 이동 없이 최초 결과를 재생합니다. 같은 키를 다른
itemId 또는 반대 action에 사용하면 HTTP 409입니다.

## 오류 코드

- `not_initialized`: 보드 또는 인벤토리가 없음
- `stale_revision`: board 또는 inventory revision 불일치
- `item_not_found`: 요청 아이템이 원본 저장소에 없음
- `inventory_full`: 보관함 용량 부족
- `full_board`: 복원할 빈 보드 슬롯 없음
- `idempotency_key_conflict`: 멱등 키를 다른 이동에 재사용
- `invalid_idempotency_key`: 키가 없거나 64자 초과

## MySQL 변경

마이그레이션 `20260815030754_AddPlayerInventory`가 다음 테이블을 추가합니다.

- `player_inventories`: 용량, revision, 수정 시각
- `inventory_items`: 보존된 itemId, chainId, level
- `inventory_transfer_receipts`: 이동 멱등 키와 최초 응답

인벤토리 revision은 EF Core 동시성 토큰입니다. 플레이어 삭제 시 인벤토리와 아이템,
영수증은 외래 키 정책에 따라 함께 제거됩니다.

## 단계 완료 검증

2026-08-15에 다음 항목을 확인했습니다.

- 보드→인벤토리→보드 왕복 이동 테스트
- 왕복 후 아이템 ID·계열·레벨 유지 테스트
- 복원 시 서버의 최저 빈 슬롯 선택 테스트
- 같은 멱등 키 보관 재시도의 단일 이동 테스트
- 오래된 inventory revision의 보드 무변경 테스트
- 부트스트랩의 기본 20칸 인벤토리 생성 테스트
- 콘텐츠 카탈로그 인벤토리 용량 테스트
- 인벤토리 3개 OpenAPI 경로 계약 검사
- 전체 자동 테스트 76개 통과
- Release 빌드 경고 0개, 오류 0개
- EF Core 모델과 마이그레이션 동기화 확인

## 다음 단계

23단계에서는 운영 배포를 위한 CI 검증, 서버·클라이언트 버전 정책, 관리자 역할 기반 접근과
운영 런북을 보강합니다.
