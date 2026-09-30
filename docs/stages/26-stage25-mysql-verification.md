# SV-026 — Stage 25 MySQL 통합·동시성 검증

## 검증 범위와 실행

`MySqlStage25Tests`는 MySQL 8.0.36에 매 테스트마다 고유한 `mergegame_sv026_*` DB를 생성하고
종료 시 그 DB만 삭제합니다. 연결 문자열은 `SV026_MYSQL_ADMIN` 환경 변수로만 받습니다.
실행하지 않은 검사를 통과로 표시하지 않도록 환경 변수가 없으면 5개 테스트가 명시적으로 skip됩니다.
GitHub CI는 MySQL 8 서비스의 root 연결을 주입하므로 이 5개를 실제로 실행합니다.

| 항목 | 실제 MySQL 검사 |
|---|---|
| 빈 DB → 최신 Migration | 모든 EF Migration 적용, pending 없음, 최신 Migration 확인 |
| Stage 24 → Stage 25 | `AddAdminApprovalWorkflow`까지 적용 후 `AddItemDiscoveries` 적용 |
| Collection Backfill | 현재 보드·인벤토리 UNION, 중복 제거, 사라진 과거 아이템 미복원 |
| Generator + Discovery | 아이템·에너지·충전·영수증·원장·발견 함께 저장 |
| Merge + Discovery | 소비된 Lv01과 결과 Lv02 발견 유지 |
| Sell 이후 Discovery | 아이템 삭제 뒤 발견 행 유지 |
| Inventory 이동 이후 Discovery | 보관·복원 후 발견 행 유지 |
| Duplicate Discovery | 동시 INSERT 한 건만 성공, 복합 PK로 중복 차단 |
| 동일 Idempotency Key | 동시 생성 후 아이템·비용·충전·영수증 한 번만 적용, 재시도 응답 재생 |
| Revision Conflict | 오래된 보드·경제 revision 생성 실패와 상태 무변경 |
| Transaction Rollback | 도감 INSERT 실패를 강제해 아이템·경제·충전·영수증·원장·발견 모두 원상복구 |

동시 생성 검증에서 MySQL 데드락이 Pomelo의 일시적 오류 래퍼로 올라와 HTTP 500이 될 수 있는
기존 결함을 확인했습니다. 생성 서비스가 해당 MySQL 데드락만 포착해 영수증을 다시 조회하고,
아직 승자 커밋 전이면 기존 `stale_revision` 충돌로 반환하도록 수정했습니다. 신규 API나 DB
스키마는 없습니다. [v1 오류 계약](../contracts/api-errors-v1.md)에 재시도 규칙을 기록했습니다.

로컬 재현은 격리된 MySQL 8.0.36 컨테이너의 127.0.0.1:3307에서 수행했습니다.
동일한 검사가 CI의 MySQL 8 서비스에서도 실행되며 OpenAPI는
[버전 관리 스냅샷](../contracts/openapi-v1.json)과 비교됩니다. 운영 DB에는 Migration을 적용하지
않았습니다.
2026-09-30 Release 전체 테스트 결과는 **94개 통과, 실패·건너뜀 0개**입니다.

## SV-CONTRACT-001 — `POST /api/v1/board/`

현재 의미는 인증 플레이어의 **보드 최초 초기화**입니다. 요청 본문은 없고, 서버가 5×7 보드와
`garden` Lv01 두 개를 슬롯 0·1에 만듭니다. 첫 호출은 201, 기존 보드에는 상태 변경 없이 200을
반환합니다. 인증 플레이어가 삭제됐으면 404 `player_not_found`입니다. 클라이언트는 보드 크기,
초기 아이템 또는 슬롯을 정할 수 없으므로 현재 서버 권위 정책에 적합합니다. 다만 Bootstrap도
동일한 초기 보드를 만들기 때문에 초기화 경로가 두 개입니다. 이번 조사에서는 API를 변경하지
않았습니다. 클라이언트가 Bootstrap만 사용하도록 정리한 뒤 별도 API 유지·축소 여부를 PD가
결정하면 됩니다.

## SV-CONTRACT-002 — 두 guest Endpoint

`POST /api/v1/players/guest`는 본문 없이 **새 계정**을 만들고 201과
`{ playerId, displayName, guestToken, createdAtUtc }`를 반환합니다. 원본 `guestToken`은 이때만
전달됩니다. `POST /api/v1/auth/guest`는 `{ playerId, guestToken }`을 검증해 **기존 계정에 로그인**하고
200과 `{ playerId, accessToken, tokenType, expiresAtUtc, refreshToken,
refreshTokenExpiresAtUtc }`를 반환합니다. 두 경로는 계정 생성과 인증으로 역할이 달라 중복
Endpoint가 아닙니다. 이번 조사에서 API나 인증 구조를 변경하지 않았습니다.

조사 중 `players/guest`의 201 응답 `Location: /api/v1/players/{playerId}`가 실제 등록된
GET 경로를 가리키지 않는 것을 확인했습니다. 현재 조회 경로는 인증된 `GET /api/v1/players/me`입니다.
클라이언트는 `Location`을 조회 API로 사용하지 말아야 하며, 수정 여부는 PD 결정 후 별도
계약 변경으로 처리합니다.

## PD 결정 사항 반영

- Production Board는 5×7로 유지합니다.
- Backfill은 현재 보드와 보관함에서 확인 가능한 아이템만 사용합니다.
- 과거 판매·소비 아이템을 추정해 복원하지 않습니다.
- `/api/v1/economy/generate`는 유지합니다.
- OpenAPI v1 JSON을 저장소에 고정하고 CI에서 변경을 감지합니다.
