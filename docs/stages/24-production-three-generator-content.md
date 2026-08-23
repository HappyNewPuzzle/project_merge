# 24단계 — Production Toy·Food·Rest Generator 콘텐츠

## 단계 목표

기존 서버 권위형 생성 흐름을 변경하지 않고 Production에서 사용할 `toy_basic`,
`food_basic`, `rest_basic` 생성기와 각 Lv01~08 머지 체인을 서버 콘텐츠 카탈로그에
추가합니다. 클라이언트는 generator ID와 동시성·멱등 정보만 보내며 결과 아이템, 비용,
대상 슬롯은 계속 서버가 결정합니다.

## 변경 전 구조 분석

- `IGeneratorCatalog`는 ID 조회와 전체 정의 조회를 제공하고 구현체에는 `garden`만 있었습니다.
- `IItemCatalog`에는 `garden` Lv01~05만 있었으며 toy, food, rest 체인은 없었습니다.
- `ProduceGeneratorItemService`는 영수증 조회, Catalog 조회, 보드·경제 조회, Generator 지연 생성,
  revision 검증, 빈 슬롯 선택, 에너지·충전 검증, 아이템 생성, 원장·퀘스트·영수증 저장을 한 번의
  `SaveChanges`로 처리하고 있었습니다.
- Bootstrap은 Catalog에 있지만 플레이어 DB에 없는 Generator 상태를 최대 충전으로 보충합니다.
- Content Catalog는 Item과 Generator Catalog의 실제 정의를 공개 DTO로 변환합니다.

## Generator 정의

| Generator ID | 출력 | Energy Cost | Max Charges | Charge Recovery |
|---|---|---:|---:|---:|
| `garden` | `garden` Lv01 | 1 | 5 | 30초 |
| `toy_basic` | `toy` Lv01 | 1 | 5 | 30초 |
| `food_basic` | `food` Lv01 | 1 | 5 | 30초 |
| `rest_basic` | `rest` Lv01 | 1 | 5 | 30초 |

신규 3종의 초기값은 기존 Production 경로와 테스트에서 검증된 `garden` 값을 기준으로
선택했습니다. 정의는 각각 독립된 `GeneratorDefinition` 인스턴스이므로 이후 에너지 비용,
최대 충전량, 회복 시간을 Generator별로 조정할 수 있습니다. 기존 `garden`은 호환성을 위해
삭제하거나 변경하지 않았습니다.

## Item Catalog

`toy`, `food`, `rest`에 각각 Lv01~08을 추가했습니다. Lv01~07은 머지 가능하고 Lv08만
`IsMaxLevel=true`입니다. 판매가는 현재 경제 규칙과 같은 단계별 두 배 계열인
5, 10, 20, 40, 80, 160, 320, 640 코인을 사용합니다.

서버에는 Unity Sprite 이름이나 Asset 경로를 저장하지 않습니다. `chainId`와 `level`이 게임
판정용 안정 식별자이고, 영문 `name`은 Content Catalog의 표시 메타데이터입니다.

## 최종 생성 API 계약

```http
POST /api/v1/board/generators/{generatorId}/produce
Authorization: Bearer {accessToken}
Content-Type: application/json
```

허용되는 `{generatorId}`는 `garden`, `toy_basic`, `food_basic`, `rest_basic`입니다.

```json
{
  "expectedBoardRevision": 1,
  "expectedEconomyRevision": 1,
  "idempotencyKey": "session-42:produce:000001"
}
```

요청에는 `itemId`, `chainId`, `level`, `energyCost`, `cost`, `targetSlot`이 없습니다.

```json
{
  "board": {
    "playerId": "00000000-0000-0000-0000-000000000001",
    "width": 5,
    "height": 7,
    "revision": 2,
    "items": []
  },
  "economy": {
    "playerId": "00000000-0000-0000-0000-000000000001",
    "energy": 99,
    "maxEnergy": 100,
    "coins": 0,
    "revision": 2,
    "nextEnergyAtUtc": "2026-08-23T03:05:00Z",
    "dailyRewardClaimedToday": false
  },
  "generatedItem": {
    "itemId": "00000000-0000-0000-0000-000000000002",
    "slotIndex": 2,
    "chainId": "toy",
    "level": 1,
    "name": "Toy Block",
    "isMaxLevel": false
  },
  "targetSlot": 2,
  "generator": {
    "generatorId": "toy_basic",
    "charges": 4,
    "maxCharges": 5,
    "isCoolingDown": false,
    "nextChargeAtUtc": "2026-08-23T03:00:30Z",
    "cooldownRemainingSeconds": 30,
    "revision": 2,
    "chargeUpdatedAtUtc": "2026-08-23T03:00:00Z"
  },
  "replayed": false
}
```

## 서버 처리와 동시성

1. JWT에서 인증된 player ID를 사용합니다.
2. `(playerId, idempotencyKey)` 성공 영수증을 revision보다 먼저 조회합니다.
3. URL의 generator ID를 서버 Catalog 정의로 변환합니다.
4. 플레이어 소유 보드·경제·해당 Generator 상태만 조회합니다.
5. Generator 상태가 없으면 최대 충전·revision 1로 지연 생성합니다.
6. 보드와 경제 revision을 함께 검증합니다.
7. 서버가 가장 낮은 빈 슬롯을 선택합니다.
8. 서버 정의의 에너지 비용과 충전 상태를 검증합니다.
9. 서버 정의의 chain/level로 아이템을 만들고 세 상태의 revision을 증가시킵니다.
10. 아이템, 상태, 경제 원장, 퀘스트 진행, 성공 영수증을 원자 저장합니다.

보드 또는 경제 revision 하나라도 다르면 HTTP 409 `stale_revision`이며 아이템, 에너지,
Generator 충전 어느 것도 변경하지 않습니다. `PlayerGenerator.Revision`은 EF concurrency token이라
동일 Generator에 대한 동시 소비도 DB 저장 단계에서 차단됩니다. 다른 Generator는 composite key가
다르므로 선택한 Generator의 충전만 감소합니다.

## 멱등성과 오류

- 동일 player, key, generator 재시도: 최초 응답을 `replayed=true`로 재생
- 동일 player와 key를 다른 generator에 사용: HTTP 409 `idempotency_key_conflict`
- 미등록 generator: HTTP 404 `unknown_generator`
- 초기 상태 없음: HTTP 404 `not_initialized`
- 잘못된 멱등 키: HTTP 400 `invalid_idempotency_key`
- 보드 가득 참: HTTP 422 `full_board`
- 에너지 부족: HTTP 422 `insufficient_energy`
- 충전 부족: HTTP 422 `generator_cooldown`
- 보드 또는 경제 revision 불일치: HTTP 409 `stale_revision`

실패와 재생 경로에서 아이템·에너지·충전은 이중 변경되지 않습니다. 엔드포인트는 기존 Board JWT
그룹과 정지 계정 미들웨어를 그대로 사용하며 관리자 `X-Admin-Key` 경로와 무관합니다.

## Content Catalog와 Bootstrap

`GET /api/v1/content/catalog`의 `generators`에 4개 정의의 `generatorId`,
`generatedChainId`, `generatedLevel`, `energyCost`, `maxCharges`,
`chargeRecoverySeconds`가 노출됩니다. `itemChains`에는 신규 세 체인의 Lv01~08이 노출됩니다.

`POST /api/v1/game/bootstrap`은 기존 플레이어에게도 누락된 신규 Generator 상태를 최대 충전으로
지연 추가하며 응답 `generators`에 네 상태를 반환합니다. 콘텐츠 변경을 알리기 위해
`GameContentVersion`을 `2026.08.23.0`으로 올렸습니다.

## DB Migration과 기존 데이터 호환성

Migration은 추가하지 않았습니다. 기존 `player_generators` 기본 키가
`(player_id, generator_id)`이고 ID 컬럼 길이 32자가 새 ID를 모두 수용합니다. 정의는 코드
Catalog 데이터이며 테이블 스키마가 아닙니다. 기존 플레이어는 Bootstrap 또는 최초 produce에서
새 상태를 안전하게 얻고 기존 `garden` 상태와 영수증은 그대로 유지됩니다.

## 자동 검증

- `toy_basic`, `food_basic`, `rest_basic`이 각각 해당 체인의 Lv01 생성
- 서버 최저 빈 슬롯 선택, 에너지 차감, 보드·경제·Generator revision 증가
- 선택하지 않은 Generator의 충전과 revision 유지
- 동일 성공 요청 재생과 다른 Generator ID의 멱등 키 충돌
- stale board/economy에서 모든 상태 무변경
- unknown, full board, insufficient energy, cooldown 기존 분기 유지
- Content Catalog에 네 Generator와 신규 Lv01~08 체인 노출
- Bootstrap에서 기존 플레이어의 네 Generator 상태 보충
- OpenAPI 요청 DTO에 서버 권위 필드가 없음을 검사
- Release 빌드 경고 0개, 전체 자동 테스트 86개 통과

## Unity 후속 단계

실제 계약을 기준으로 작성한 클라이언트 작업 지시는
[Unity Production Generator 후속 프롬프트](../prompts/unity-production-three-generators.md)에 있습니다.
Toy 화면은 기존 로컬 생성 계약을 새 API로 우선 전환하고, Food/Rest 전용 화면은 아트와 UX 범위를
확정한 별도 Step으로 분리하는 것을 권장합니다.
