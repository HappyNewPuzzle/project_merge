# 19단계 — 서버 권위형 아이템 판매와 공간 관리

## 단계 목표

보드가 가득 찼을 때 불필요한 아이템을 판매해 슬롯을 확보하고 코인을 받을 수 있습니다.
아이템 존재 여부와 판매 가격은 서버가 결정하며, 보드 제거와 코인 지급을 같은 트랜잭션에
저장합니다.

## API

```http
POST /api/v1/board/items/{itemId}/sell
Authorization: Bearer {accessToken}
Content-Type: application/json
```

```json
{
  "expectedBoardRevision": 5,
  "expectedEconomyRevision": 3,
  "idempotencyKey": "device-session-42:sell:000001"
}
```

클라이언트는 판매 가격이나 슬롯을 보내지 않습니다. `itemId`가 현재 인증 플레이어의
보드에 있는지 확인하고 서버 카탈로그의 현재 레벨 가격을 사용합니다.

성공 응답:

- 판매 후 전체 `board`와 새 board revision
- 코인 지급 후 전체 `economy`와 새 economy revision
- 제거된 `soldItem`
- 서버가 적용한 `salePrice`
- 멱등 재생 여부 `replayed`

## 판매 가격

현재 `garden` 체인의 가격은 다음과 같습니다.

| 레벨 | 아이템 | 판매 코인 |
|---:|---|---:|
| 1 | Seed Bag | 5 |
| 2 | Green Sprout | 10 |
| 3 | Flower Pot | 20 |
| 4 | Flower Basket | 40 |
| 5 | Garden Arch | 80 |

`SellPrice`는 `ItemDefinition`의 일부이며 18단계 공개 콘텐츠 카탈로그에도 포함됩니다.
Unity는 표시에는 카탈로그 가격을 사용할 수 있지만 최종 지급액은 항상 판매 성공 응답을
기준으로 처리해야 합니다.

## 원자성과 멱등성

한 번의 `SaveChanges` 트랜잭션에 다음 변경이 포함됩니다.

1. 판매 아이템을 보드 컬렉션에서 제거
2. 보드 revision 증가
3. 서버 판매 가격만큼 코인 지급
4. 경제 revision 증가
5. 최초 성공 응답 멱등 영수증 추가

보드 또는 경제 revision이 하나라도 오래됐으면 어떤 변경도 적용하지 않습니다.
`board_item_sale_receipts`의 `(player_id, idempotency_key)` 유니크 키가 동시 재시도를
차단합니다. 같은 키 재시도는 최초 성공 응답만 재생하고 코인을 다시 지급하지 않습니다.
같은 키를 다른 itemId에 사용하면 HTTP 409입니다.

## 오류 코드

| HTTP | code | 의미 |
|---|---|---|
| 400 | `invalid_idempotency_key` | 키가 없거나 64자 초과 |
| 404 | `not_initialized` | 보드 또는 경제가 없음 |
| 404 | `item_not_found` | 아이템이 현재 보드에 없음 |
| 409 | `stale_revision` | 보드 또는 경제 revision 불일치 |
| 409 | `idempotency_key_conflict` | 같은 키를 다른 아이템에 사용 |
| 422 | `unknown_item_definition` | 카탈로그에 없는 아이템 |
| 422 | `item_not_sellable` | 판매 가격이 없거나 0 이하 |

## MySQL 변경

마이그레이션 `20260815025205_AddIdempotentBoardItemSales`가
`board_item_sale_receipts` 테이블과 플레이어 단위 멱등 유니크 인덱스를 추가합니다.
성공 응답 JSON을 저장하므로 판매 후 아이템이 사라져도 최초 결과를 정확히 재생할 수 있습니다.

## 단계 완료 검증

2026-08-15에 다음 항목을 확인했습니다.

- 1레벨 아이템 제거와 코인 5 지급 테스트
- 보드·경제 revision 동시 증가 테스트
- 같은 멱등 키 재시도 시 단일 지급 테스트
- 존재하지 않는 itemId의 경제 무변경 테스트
- 오래된 경제 revision의 보드 무변경 테스트
- 콘텐츠 카탈로그 레벨별 판매 가격 테스트
- 판매 OpenAPI 경로·요청·응답 계약 검사
- 전체 자동 테스트 68개 통과
- Release 빌드 경고 0개, 오류 0개
- EF Core 모델과 마이그레이션 동기화 확인

## 다음 단계

20단계에서는 코인과 에너지 변경 사유를 영구 추적하는 통합 경제 원장을 추가합니다.
