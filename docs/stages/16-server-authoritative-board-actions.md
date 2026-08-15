# 16단계 — 서버 권위형 보드 이동·교환·머지 액션

## 단계 목표

Unity의 드래그 앤 드롭이 액션 종류를 임의로 결정하지 않도록, 원본 슬롯과 대상 슬롯만
받아 서버가 `move`, `merge`, `swap` 중 실제 동작을 결정하는 통합 API를 추가했습니다.
기존 `POST /api/v1/board/merge`는 배포된 클라이언트 호환을 위해 유지합니다.

## API 계약

```http
POST /api/v1/board/actions
Authorization: Bearer {accessToken}
Content-Type: application/json
```

```json
{
  "sourceSlot": 0,
  "targetSlot": 8,
  "expectedBoardRevision": 3,
  "idempotencyKey": "device-session-42:board-action:000001"
}
```

서버 판정 규칙은 다음 순서입니다.

1. 대상 슬롯이 비었으면 아이템 인스턴스를 `move`합니다.
2. 대상 아이템이 같은 계열·같은 레벨이고 다음 레벨이 있으면 `merge`합니다.
3. 서로 다른 아이템이면 두 인스턴스의 슬롯을 `swap`합니다.
4. 같은 최대 레벨 아이템은 `max_level_reached`로 거부합니다.

성공 응답에는 전체 `board`, 실제 `action`, 두 슬롯, 애니메이션 기준 `resultItem`,
멱등 재생 여부가 포함됩니다. Unity는 로컬에서 결과를 추측하지 않고 전체 보드 상태를
응답 값으로 교체해야 합니다.

## 멱등성과 동시성

`board_action_receipts`는 성공 응답 JSON과 source/target 슬롯을 저장합니다.
`(player_id, idempotency_key)` 유니크 키로 동시에 도착한 같은 요청도 한 번만 반영합니다.
네트워크 타임아웃 재시도는 최초 응답을 `replayed: true`로 반환합니다. 같은 키에 다른
슬롯을 보내면 HTTP 409 `idempotency_key_conflict`입니다.

보드 revision은 이동·머지·교환 모두 성공할 때 1 증가합니다. 오래된 revision은 HTTP 409
`stale_revision`으로 거부하며 현재 보드를 함께 반환합니다.

## 머지 이벤트 호환

통합 액션이 `merge`로 판정되면 기존 머지 API와 동일하게 다음 변경도 같은 트랜잭션에
포함합니다.

- `item_merged` 게임플레이 이벤트 추가
- 첫 머지 퀘스트 진행도 증가
- 보드 원본 아이템 삭제와 대상 레벨 상승
- 보드 액션 멱등 영수증 추가

이동과 교환은 머지 퀘스트 진행도를 올리지 않습니다.

## MySQL 슬롯 제약 변경

MySQL의 `(player_id, slot_index)` 유니크 인덱스는 두 아이템 슬롯을 서로 바꾸는 UPDATE를
중간 중복 상태로 판정할 수 있습니다. 16단계 마이그레이션은 이 유니크 인덱스를 일반
`player_id` 인덱스로 변경합니다.

슬롯 범위 CHECK 제약은 그대로 유지합니다. 중복 슬롯 불변식은 `PlayerBoard` 애그리게이트가
모든 변경을 검증하고, `player_boards.revision` 동시성 토큰이 같은 보드의 병렬 쓰기 중
하나만 성공하게 해 보장합니다. 보드 아이템을 애그리게이트 밖에서 직접 수정하면 안 됩니다.

마이그레이션:

- `20260815024154_AddIdempotentBoardActions`
- `board_action_receipts` 테이블 추가
- 기존 슬롯 유니크 인덱스를 플레이어 조회 인덱스로 변경

## 오류 코드

| HTTP | code | 의미 |
|---|---|---|
| 400 | `invalid_idempotency_key` | 키가 없거나 64자 초과 |
| 404 | `board_not_initialized` | 보드가 생성되지 않음 |
| 409 | `stale_revision` | 보드 revision 불일치 |
| 409 | `idempotency_key_conflict` | 같은 키에 다른 슬롯 사용 |
| 422 | `invalid_slot` | 0~34 범위 밖 슬롯 |
| 422 | `same_slot` | source와 target이 동일 |
| 422 | `empty_source_slot` | 원본 슬롯이 비어 있음 |
| 422 | `unknown_item_definition` | 서버 카탈로그에 없는 아이템 |
| 422 | `max_level_reached` | 같은 최대 레벨 아이템 머지 시도 |

## 단계 완료 검증

2026-08-15에 다음 항목을 확인했습니다.

- 이동 시 아이템 ID 유지와 revision 증가 테스트
- 같은 아이템의 서버 판정 머지 테스트
- 서로 다른 아이템 인스턴스 슬롯 교환 테스트
- 머지 액션의 게임플레이 이벤트 저장 테스트
- 같은 멱등 키 재시도의 단일 적용 테스트
- 같은 키에 다른 슬롯을 보낸 충돌 테스트
- 신규 및 기존 보드 API OpenAPI 계약 검사
- 전체 자동 테스트 59개 통과
- Release 빌드 경고 0개, 오류 0개
- EF Core 모델과 마지막 마이그레이션 동기화 확인

## Unity 적용 순서

1. 드래그 시작 시 source 슬롯을 기억합니다.
2. 드롭 시 target 슬롯과 현재 board revision, 새 멱등 키를 전송합니다.
3. 성공 응답의 `action`으로 이동·머지·교환 애니메이션을 선택합니다.
4. 애니메이션 종료 후 응답의 전체 `board`로 로컬 상태를 교체합니다.
5. 타임아웃은 같은 키로 재시도합니다.
6. `stale_revision`은 최신 보드를 반영하고 사용자 조작을 취소합니다.

## 다음 단계

17단계에서는 로그인 직후 여러 초기화·조회 API를 하나로 묶는 게임 부트스트랩 API를
추가합니다.
