# 21단계 — 일일·주간 다중 퀘스트

## 단계 목표

기존 일회성 첫 머지 퀘스트를 유지하면서 생성, 판매, 친구 선물과 주간 머지를 추적하는
다중 퀘스트 시스템으로 확장했습니다. 기간 경계와 진행 이벤트는 클라이언트 시간이 아니라
서버 UTC를 기준으로 판정합니다.

## 활성 퀘스트

| questId | 기간 | 서버 이벤트 | 목표 | 코인 보상 |
|---|---|---|---:|---:|
| `merge_3` | lifetime | `item_merged` | 3 | 100 |
| `daily_generate_5` | daily | `item_generated` | 5 | 30 |
| `daily_sell_3` | daily | `item_sold` | 3 | 40 |
| `daily_friend_gift_1` | daily | `friend_energy_sent` | 1 | 25 |
| `weekly_merge_20` | weekly | `item_merged` | 20 | 250 |

퀘스트 정의는 `IQuestCatalog`가 제공하며 18단계 공개 콘텐츠 응답에도 포함됩니다. 규칙이
추가되어 콘텐츠 버전은 `2026.08.15.2`로 올렸습니다.

## 기간 키

각 플레이어 퀘스트는 `periodType`과 `periodKey`를 저장합니다.

- lifetime: `lifetime`
- daily: UTC 날짜 `yyyy-MM-dd`
- weekly: 해당 UTC 주의 월요일 날짜 `yyyy-MM-dd`

조회·부트스트랩·게임 이벤트 시 저장된 키가 현재 키와 다르면 진행도, 완료 시각과 수령
상태를 초기화하고 revision을 증가시킵니다. 일회성 퀘스트는 기간 키가 바뀌지 않습니다.

## 서버 확정 이벤트 연결

`QuestProgressService`는 다음 성공 트랜잭션 안에서만 진행도를 변경합니다.

- 기존 및 서버 권위형 아이템 생성 성공 → `item_generated`
- 기존 및 통합 보드 머지 성공 → `item_merged`
- 아이템 판매와 코인 지급 성공 → `item_sold`
- 친구 에너지 선물 성공 → `friend_energy_sent`

실패, revision 충돌, 멱등 재생은 진행도를 추가하지 않습니다. 대상 이벤트를 구독하는
퀘스트가 아직 없는 이전 계정에는 현재 기간 행을 지연 생성해 기존 클라이언트도 지원합니다.

## API 변경

기존 경로를 유지하되 초기화와 조회 응답이 단일 객체에서 배열로 확장됐습니다.

```http
POST /api/v1/quests/
GET /api/v1/quests/
```

각 `QuestSnapshot`에는 다음 기간 필드가 추가됩니다.

- `eventType`
- `periodType`
- `periodKey`

보상 수령 경로는 유지됩니다.

```http
POST /api/v1/quests/{questId}/claim
```

퀘스트 완료·퀘스트 revision·경제 revision을 검증하고, 코인 지급·보상 영수증·경제 원장을
한 트랜잭션으로 저장합니다. 반복 퀘스트는 새 기간에 새 멱등 키를 사용해야 합니다.

## MySQL 변경

마이그레이션 `20260815030244_ExpandRecurringQuests`가 기존 `player_quests`에 다음 컬럼을
추가합니다.

- `event_type varchar(32)`
- `period_type varchar(16)`
- `period_key varchar(16)`

기존 `merge_3` 행은 최초 조회 또는 이벤트 처리 때 lifetime 정의로 자동 보정됩니다.

## 단계 완료 검증

2026-08-15에 다음 항목을 확인했습니다.

- 5개 활성 퀘스트 초기화 테스트
- 이벤트 유형과 일치하는 퀘스트만 진행되는 테스트
- 일일 기간 변경 시 진행·완료·수령 상태 초기화 테스트
- UTC 월요일 주간 키 경계 테스트
- 다음 UTC 날짜에 일일만 초기화되고 lifetime 진행도는 유지되는 테스트
- 생성·머지·판매·친구 선물 서비스의 실제 진행 연결 테스트
- 퀘스트 보상과 경제 원장의 멱등 단일 지급 회귀 테스트
- 콘텐츠 카탈로그의 퀘스트 정의 노출 테스트
- 전체 자동 테스트 73개 통과
- Release 빌드 경고 0개, 오류 0개
- EF Core 모델과 마이그레이션 동기화 확인

## 다음 단계

22단계에서는 보드 밖에 아이템을 보관하고 원자적으로 왕복 이동할 수 있는 인벤토리를
추가합니다.
