# Unity 클라이언트 후속 구현 프롬프트

아래 내용을 새 Unity 클라이언트 작업 세션에 그대로 전달하세요.

---

Project Merge Unity 클라이언트에서 서버 권위형 Production Generator 연동을 구현해주세요.

클라이언트 저장소만 수정하세요.

- Unity 저장소: `C:\Users\happy\mergegame-client`
- 서버 저장소는 읽기 전용 참고: `C:\Users\happy\mergegame`
- 서버 파일을 수정하거나 클라이언트 커밋에 포함하지 마세요.
- 작업 전 클라이언트의 `AGENTS.md`, README, Docs/AI 상태 문서, 네트워크 DTO/API, Bootstrap,
  Board/Economy/Generator 상태 저장소, Toy Generator UI, Offline Mock과 관련 테스트를 확인하세요.

## 확정 서버 계약

Endpoint:

```http
POST /api/v1/board/generators/{generatorId}/produce
Authorization: Bearer {accessToken}
Content-Type: application/json
X-Client-Version: {clientVersion}
```

실제 Generator ID와 결과:

- `toy_basic` → `toy` Lv01
- `food_basic` → `food` Lv01
- `rest_basic` → `rest` Lv01
- 기존 호환용 `garden` → `garden` Lv01

세 Production Generator의 현재 서버 규칙은 에너지 비용 1, 최대 충전 5,
30초마다 충전 1회 회복입니다. 이 값을 클라이언트 상수로 게임 판정하지 말고 Content Catalog를
표시와 선행 UI 검사에만 사용하세요. 최종 성공·실패 판정은 서버 응답입니다.

Request JSON은 정확히 다음 세 필드만 사용합니다.

```json
{
  "expectedBoardRevision": 1,
  "expectedEconomyRevision": 1,
  "idempotencyKey": "device-session-42:produce:000001"
}
```

`itemId`, `chainId`, `level`, `energyCost`, `cost`, `targetSlot`을 요청에 추가하지 마세요.

성공 Response JSON 구조:

```json
{
  "board": {
    "playerId": "00000000-0000-0000-0000-000000000001",
    "width": 5,
    "height": 7,
    "revision": 2,
    "items": [
      {
        "itemId": "00000000-0000-0000-0000-000000000002",
        "slotIndex": 2,
        "chainId": "toy",
        "level": 1,
        "name": "Toy Block",
        "isMaxLevel": false
      }
    ]
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

오류 Response는 `{ code, message, board, economy, generator }`이며 계약은 다음과 같습니다.

- 400 `invalid_idempotency_key`
- 404 `unknown_generator`
- 404 `not_initialized`
- 409 `stale_revision`
- 409 `idempotency_key_conflict`
- 422 `full_board`
- 422 `insufficient_energy`
- 422 `generator_cooldown`
- 기존 인증 실패 401, 정지 계정 403, 최소 클라이언트 버전 실패 426도 유지

## Content Catalog와 Bootstrap

`GET /api/v1/content/catalog`의 각 `generators[]` 필드:

```json
{
  "generatorId": "toy_basic",
  "generatedChainId": "toy",
  "generatedLevel": 1,
  "energyCost": 1,
  "maxCharges": 5,
  "chargeRecoverySeconds": 30
}
```

`itemChains[]`에는 `chainId`와 `levels[]`가 있고, 각 level은
`level`, `name`, `isMaxLevel`, `sellPrice`를 가집니다. `toy`, `food`, `rest`는 Lv01~08이며
Lv08만 최대 레벨입니다. 현재 콘텐츠 버전은 `2026.08.23.0`입니다.

`POST /api/v1/game/bootstrap` 응답의 `generators[]`는 다음 상태 필드를 가집니다.

```json
{
  "generatorId": "toy_basic",
  "charges": 5,
  "maxCharges": 5,
  "isCoolingDown": false,
  "nextChargeAtUtc": null,
  "cooldownRemainingSeconds": 0,
  "revision": 1,
  "chargeUpdatedAtUtc": "2026-08-23T03:00:00Z"
}
```

## 구현 규칙

1. 기존 Toy Generator가 item/slot/cost를 로컬 결정하거나 구형 `/api/v1/economy/generate`를
   사용한다면 `toy_basic/produce`로 전환하세요.
2. 한 번의 사용자 클릭마다 새로운 1~64자 idempotencyKey를 만드세요. 타임아웃·연결 끊김처럼
   결과를 모르는 동일 동작 재시도에만 같은 키를 재사용하세요.
3. 성공하면 로컬에서 에너지나 충전을 계산해 빼지 말고 응답 `board`, `economy` 전체와
   해당 `generator` 상태로 교체하세요. `generatedItem`과 `targetSlot`은 연출에 사용하세요.
4. `replayed=true` 응답도 성공 상태로 적용하되 생성 연출이 중복 재생되지 않도록 현재 UX 규칙을
   명시하고 테스트하세요.
5. 409 `stale_revision`이면 Bootstrap 또는 관련 최신 상태를 다시 동기화하고, 사용자의 새 생성
   동작에는 새 key를 사용하세요. 같은 key를 다른 Generator ID에 절대 재사용하지 마세요.
6. 쿨다운 표시는 서버 UTC의 `nextChargeAtUtc`와 서버 시간 기준으로 계산하고 기기 시각을
   권위 값으로 사용하지 마세요.
7. Offline Mock도 동일 Request/Response/오류 구조, Generator ID, 서버 최저 빈 슬롯 선택,
   에너지·충전·revision 증가, 멱등 재생 규칙으로 정렬하세요.
8. Food/Rest는 DTO·상태 저장·Mock·콘텐츠 매핑까지 이번 Step에서 지원하세요. 전용 UI와 아트가
   아직 없다면 임시 Toy UI 복제는 하지 말고 별도 UI Step으로 문서화하세요.

## 테스트

- Toy 클릭이 `toy_basic` 경로와 현재 board/economy revision을 전송
- 요청 DTO에 item/chain/level/cost/targetSlot이 없음
- 성공 응답으로 Board/Economy/선택 Generator 상태 전체 교체
- Food/Rest Bootstrap 및 Content Catalog 역직렬화
- 같은 key 타임아웃 재시도와 `replayed=true` 처리
- 다른 Generator에 같은 key를 쓰지 않음
- stale revision 재동기화, full board, energy 부족, cooldown UI 처리
- Offline Mock이 toy/food/rest Lv01과 revision·충전 규칙을 서버와 동일하게 처리
- 기존 인증 갱신, 정지 계정 403, 426 업데이트 흐름 회귀 없음

코드에는 서버 권위 상태를 로컬 추측값과 구분하는 상세 주석을 추가하고, 완료 후 클라이언트 전용
단계 문서를 작성하세요. 전체 Unity EditMode/PlayMode 또는 현재 저장소의 자동 검증을 실행하세요.
변경 파일과 테스트를 요약한 뒤 클라이언트 저장소에만 별도 커밋하고 해당 저장소 main에 푸시하세요.
서버 저장소에서 Git add/commit/push를 실행하지 말고 강제 push와 reset --hard를 사용하지 마세요.

---
