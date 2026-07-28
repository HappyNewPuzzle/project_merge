# 5단계 — 에너지·재화·아이템 생성기

## 단계 목표

클라이언트가 조작할 수 없는 서버 시간 기준 에너지, 코인, 일일 보상과 아이템
생성기를 구현합니다. 경제 상태와 보드는 각각 revision을 가지며 생성기 요청은
두 상태를 하나의 DB 트랜잭션으로 변경합니다.

## 기본 규칙

| 항목 | 값 |
|---|---:|
| 최대 에너지 | 100 |
| 에너지 충전 | 5분마다 1 |
| 아이템 생성 비용 | 에너지 1 |
| 생성 아이템 | `garden` 1레벨 `Seed Bag` |
| 일일 보상 | 코인 50 |
| 일일 기준 | UTC 날짜 |

최대 에너지인 동안 지난 시간은 비축하지 않습니다. 에너지 100 상태로 오래 지난 뒤
1을 소비해도 즉시 충전되지 않고 소비 시점부터 5분 뒤 충전됩니다.

## 경제 초기화와 조회

```http
POST /api/v1/economy/
Authorization: Bearer {accessToken}
```

반복 호출해도 재화가 다시 지급되지 않습니다.

```http
GET /api/v1/economy/
Authorization: Bearer {accessToken}
```

```json
{
  "playerId": "5a10cf89-9f74-442d-95f1-f30ca5dc8d87",
  "energy": 100,
  "maxEnergy": 100,
  "coins": 0,
  "revision": 1,
  "nextEnergyAtUtc": null,
  "dailyRewardClaimedToday": false
}
```

조회는 DB를 변경하지 않고 현재 서버 시각으로 충전량을 계산합니다.

## 아이템 생성기

```http
POST /api/v1/economy/generate
Authorization: Bearer {accessToken}
Content-Type: application/json
```

```json
{
  "targetSlot": 2,
  "expectedBoardRevision": 1,
  "expectedEconomyRevision": 1
}
```

성공 응답에는 갱신된 전체 `board`와 `economy`가 포함됩니다.

- 빈 슬롯에 서버 카탈로그의 `garden:1` 아이템 추가
- 보드 revision 1 증가
- 충전된 에너지 계산 후 1 소비
- 경제 revision 1 증가
- 모든 변경을 하나의 `SaveChanges` 트랜잭션으로 저장

revision이 오래되면 HTTP 409입니다. 슬롯이 유효하지 않거나 이미 차 있거나 에너지가
부족하면 HTTP 422이며 아무 상태도 저장하지 않습니다.

## 일일 보상

```http
POST /api/v1/economy/daily-reward
Authorization: Bearer {accessToken}
Content-Type: application/json
```

```json
{ "expectedRevision": 2 }
```

UTC 날짜 기준 첫 요청은 코인 50을 지급합니다. 같은 날짜의 두 번째 요청은
`daily_reward_already_claimed`로 거부됩니다. 기기 시각은 사용하지 않습니다.

## 오류 코드

| HTTP | code | 의미 |
|---:|---|---|
| 404 | `economy_not_initialized` | 경제 상태 초기화 필요 |
| 409 | `stale_board_revision` | 최신 보드 재조회 필요 |
| 409 | `stale_economy_revision` | 최신 경제 상태 재조회 필요 |
| 422 | `invalid_slot` | 슬롯 범위 오류 |
| 422 | `slot_occupied` | 생성 대상 슬롯이 차 있음 |
| 422 | `insufficient_energy` | 충전 후에도 에너지 부족 |
| 422 | `daily_reward_already_claimed` | 오늘 보상 수령 완료 |

## MySQL 스키마

`player_economies` 테이블은 플레이어 기본 키, 에너지, 코인, 경제 revision, 충전 기준
시각, 마지막 일일 보상 시각을 저장합니다. 에너지는 0~100, 코인은 0 이상 CHECK
제약을 적용하고 플레이어 삭제 시 FK cascade로 함께 제거합니다.

## 자동 테스트

- 최대 에너지에서 시간 비축 방지와 소비 5분 후 충전
- 같은 UTC 날짜의 일일 보상 중복 차단
- 다음 UTC 날짜의 보상 재수령
- 생성기 성공 시 아이템, 에너지, 보드·경제 revision 동시 저장

## 단계 완료 검증

2026-07-28에 다음 항목을 확인했습니다.

- 전체 솔루션 빌드: 경고 0개, 오류 0개
- 자동 테스트: 18개 통과, 실패 0개
- MySQL 마이그레이션 SQL: FK cascade, 에너지·코인 CHECK 확인
- EF 모델과 마이그레이션 일치: 추가 변경 없음
- 알려진 NuGet 취약 패키지: 0개
- 실제 MySQL API 통합 실행: 로컬 MySQL/Docker 인스턴스가 없어 미수행

EF 자동 생성 파일을 제외한 코드에는 계산 규칙과 설계 이유를 설명하는 주석을
추가했습니다.

## 다음 단계 제안

6단계에서는 퀘스트 목표, 머지 이벤트 기록, 보상 수령의 멱등성 키를 구현하는 것이
적합합니다.
