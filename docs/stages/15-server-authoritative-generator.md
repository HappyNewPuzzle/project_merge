# 15단계 — 서버 권위형 생성기 API

## 단계 목표

기존 생성 API는 클라이언트가 대상 슬롯을 지정하므로 조작된 요청과 네트워크 재시도를
별도로 방어하기 어렵습니다. 이번 단계에서는 생성기 ID와 두 revision, 멱등성 키만 받고
생성 아이템과 대상 슬롯을 서버가 결정하는 새 API를 추가했습니다.

기존 `POST /api/v1/economy/generate`는 이미 배포된 클라이언트의 호환성을 위해 그대로
유지합니다. 새 Unity 클라이언트는 서버 권위형 API를 사용해야 합니다.

## API 계약

```http
POST /api/v1/board/generators/garden/produce
Authorization: Bearer {accessToken}
Content-Type: application/json
```

```json
{
  "expectedBoardRevision": 1,
  "expectedEconomyRevision": 1,
  "idempotencyKey": "device-session-42:produce:000001"
}
```

`idempotencyKey`는 플레이어 안에서 고유한 1~64자 값이어야 합니다. 응답을 받지 못했을
때만 동일한 키를 재사용하고, 사용자가 새로 누른 생성 요청에는 반드시 새 키를 사용합니다.

성공 응답 예시는 다음과 같습니다.

```json
{
  "board": {
    "playerId": "...",
    "width": 5,
    "height": 7,
    "revision": 2,
    "items": []
  },
  "economy": {
    "playerId": "...",
    "energy": 99,
    "maxEnergy": 100,
    "coins": 0,
    "revision": 2,
    "nextEnergyAtUtc": "2026-08-11T03:05:00Z",
    "dailyRewardClaimedToday": false
  },
  "generatedItem": {
    "itemId": "...",
    "slotIndex": 2,
    "chainId": "garden",
    "level": 1,
    "name": "새싹 화분",
    "isMaxLevel": false
  },
  "targetSlot": 2,
  "generator": {
    "generatorId": "garden",
    "charges": 4,
    "maxCharges": 5,
    "isCoolingDown": false,
    "nextChargeAtUtc": "2026-08-11T03:00:30Z",
    "cooldownRemainingSeconds": 30,
    "revision": 2,
    "chargeUpdatedAtUtc": "2026-08-11T03:00:00Z"
  },
  "replayed": false
}
```

현재 `garden` 생성기는 최대 충전량 5, 충전 회복 간격 30초, 생성 비용 에너지 1이며
서버 카탈로그가 `garden` 계열 1레벨 아이템을 생성합니다. 빈 슬롯이 여러 개면 가장 낮은
슬롯 인덱스를 선택합니다. 이 값들은 클라이언트 요청으로 변경할 수 없습니다.

## 오류 계약

| HTTP | code | 의미 | 클라이언트 처리 |
|---|---|---|---|
| 404 | `unknown_generator` | 등록되지 않은 generatorId | 서버 카탈로그와 클라이언트 설정 확인 |
| 409 | `stale_revision` | 보드 또는 경제 revision 불일치 | 응답 상태 또는 조회 API로 동기화 후 새 키로 다시 요청 |
| 422 | `full_board` | 35개 슬롯이 모두 점유됨 | 머지하거나 공간을 확보 |
| 422 | `insufficient_energy` | 자연 회복을 반영해도 에너지 부족 | `nextEnergyAtUtc`까지 대기 |
| 422 | `generator_cooldown` | 생성기 충전량 0 | `nextChargeAtUtc`까지 대기 |
| 409 | `idempotency_key_conflict` | 같은 키를 다른 생성기 경로에 재사용 | 새 요청 키 생성 |

보드 또는 경제가 초기화되지 않은 경우에는 404 `not_initialized`, 키가 비었거나 64자를
초과하면 400 `invalid_idempotency_key`를 반환합니다.

## 원자성과 동시성

성공 요청은 다음 변경을 한 번의 EF Core `SaveChanges`로 MySQL에 제출합니다.

1. 빈 슬롯에 생성 아이템 추가
2. 보드 revision 증가
3. 에너지 1 차감과 경제 revision 증가
4. 생성기 충전량 1 차감과 생성기 revision 증가
5. 성공 응답 멱등 영수증 추가

EF Core 관계형 공급자는 한 `SaveChanges`의 변경을 트랜잭션으로 처리하므로 일부만 저장될
수 없습니다. 보드·경제·생성기 revision은 동시성 토큰이며, 영수증에는
`(player_id, idempotency_key)` 유니크 인덱스가 있습니다. 따라서 서로 다른 키의 동시
수정과 같은 키의 동시 재시도를 DB에서도 최종 차단합니다.

성공 응답 JSON도 트랜잭션 안의 영수증에 저장합니다. 타임아웃 뒤 같은 키로 다시 호출하면
revision이 이미 변했더라도 최초 보드·경제·아이템·생성기 상태를 `replayed: true`로
반환하며 추가 생성이나 차감은 수행하지 않습니다.

## 인증 및 정지 계정

새 경로는 기존 `/api/v1/board` 인증 그룹 안에 있어 Bearer JWT가 필수입니다. 인증 이후
`SuspendedPlayerMiddleware`를 그대로 통과하므로 정지 계정은 서비스나 DB 변경 코드에
도달하기 전에 기존과 동일한 403 응답을 받습니다. 관리자 API 키 인증에는 이 경로를
노출하지 않습니다.

## MySQL 변경

`20260811133735_AddAuthoritativeGenerators` 마이그레이션이 다음 테이블을 추가합니다.

- `player_generators`: 플레이어·생성기별 충전량, 회복 기준 시각, 동시성 revision
- `generator_production_receipts`: 멱등 키, 생성기 ID, 최초 성공 응답 JSON, 생성 시각

배포 순서는 마이그레이션 적용 후 새 서버 배포입니다. 이전 서버는 새 테이블을 사용하지
않으므로 마이그레이션 후에도 기존 `/economy/generate` 호환 경로는 계속 동작합니다.

## 단계 완료 검증

2026-08-11에 다음 항목을 확인했습니다.

- Debug/Release 빌드 경고 0개, 오류 0개
- 자동 테스트 53개 통과, 실패 0개, 건너뜀 0개
- 서버 슬롯·아이템 선택과 보드/경제/충전 상태 단일 저장 테스트
- 같은 멱등 키 재시도 시 아이템·에너지·충전량 단일 적용 테스트
- `full_board`, `insufficient_energy`, `unknown_generator`, `generator_cooldown`,
  `stale_revision` 분기 테스트
- 생성기 30초 회복 계산 테스트
- 새 경로와 기존 `/api/v1/economy/generate` OpenAPI 계약 동시 검사
- 실제 HTTP 파이프라인에서 정지 계정 403 선차단 및 DB 무변경 검사
- 신규 NuGet 패키지 0개
- 로컬 Docker 통합 검증은 Docker Desktop 데몬이 실행 중이지 않아 생략했습니다. 실제 배포 전
  `scripts/verify-docker-environment.ps1`로 MySQL 8.0.36 마이그레이션과 헬스 체크를 확인해야 합니다.

## Unity 클라이언트 적용 순서

1. 보드와 경제 초기화 응답의 revision을 로컬 상태에 보관합니다.
2. 버튼을 누를 때 새 멱등 키를 만들고 `garden/produce`를 호출합니다.
3. 성공 시 개별 로컬 변경을 추측하지 말고 응답의 전체 `board`와 `economy`로 교체합니다.
4. `generatedItem`과 `targetSlot`은 생성 애니메이션에 사용합니다.
5. 타임아웃은 같은 키로 재시도하고, 409 revision 충돌은 상태 동기화 후 새 키를 만듭니다.
6. `generator.nextChargeAtUtc`는 서버 UTC 기준으로 남은 시간을 표시하는 데 사용합니다.

## 다음 단계 제안

16단계에서는 생성기 카탈로그를 운영 설정으로 분리하고, 확률형 생성 테이블의 버전·가중치
검증 및 결과 감사 지표를 추가할 수 있습니다.
