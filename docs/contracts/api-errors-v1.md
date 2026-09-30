# v1 게임 API 오류 계약 (SV-026 확인)

아래 표는 현재 서버 Endpoint 코드가 반환하는 HTTP 상태와 `code`입니다. 대상은 도감 및 도감
발견을 유발하는 게임 API입니다. 오류 DTO는 API별로 다르며 모든 오류가 같은 JSON 형식인 것은
아닙니다. 신규 클라이언트는 HTTP 상태와 해당 Endpoint의 오류 DTO를 함께 처리해야 합니다.

| API | HTTP | `code` 또는 본문 | 처리 |
|---|---:|---|---|
| `GET /api/v1/collection` | 401 | 본문 없음 | Bearer JWT 필요 |
| `GET /api/v1/collection` | 403 | 정지 계정 응답 | 계정 정지 정책 적용 |
| `GET /api/v1/collection` | 404 | 본문 없음 | JWT 플레이어가 삭제됨 |
| `POST /api/v1/game/bootstrap` | 401/403/404 | 본문 없음 또는 정지 응답 | 인증·계정 상태 확인 |
| `POST /api/v1/board/generators/{generatorId}/produce` | 400 | `invalid_idempotency_key` | 키 1~64자 확인 |
| 동일 | 404 | `unknown_generator`, `not_initialized` | ID 또는 초기화 상태 확인 |
| 동일 | 409 | `stale_revision`, `idempotency_key_conflict` | 최신 상태 재조회 또는 새 동작 키 생성 |
| 동일 | 422 | `full_board`, `insufficient_energy`, `generator_cooldown` | 공간·에너지·충전 대기 |
| `POST /api/v1/board/merge` | 404 | `board_not_initialized` | 보드 초기화 |
| 동일 | 409 | `stale_revision` | 보드 재조회 |
| 동일 | 422 | `invalid_slot`, `same_slot`, `empty_slot`, `items_do_not_match`, `unknown_item_definition`, `max_level_reached` | 입력 재검토 |
| `POST /api/v1/board/actions` | 400 | `invalid_idempotency_key` | 키 확인 |
| 동일 | 404 | `board_not_initialized` | 보드 초기화 |
| 동일 | 409 | `stale_revision`, `idempotency_key_conflict` | 보드 재조회 또는 새 동작 키 생성 |
| 동일 | 422 | `invalid_slot`, `same_slot`, `empty_source_slot`, `unknown_item_definition`, `max_level_reached` | 입력 재검토 |
| `POST /api/v1/board/items/{itemId}/sell` | 400 | `invalid_idempotency_key` | 키 확인 |
| 동일 | 404 | `not_initialized`, `item_not_found` | 상태·아이템 재조회 |
| 동일 | 409 | `stale_revision`, `idempotency_key_conflict` | 상태 재조회 또는 새 동작 키 생성 |
| 동일 | 422 | `unknown_item_definition`, `item_not_sellable` | 판매 불가 표시 |
| `POST /api/v1/inventory/store`, `/items/{itemId}/restore` | 400 | `invalid_idempotency_key` | 키 확인 |
| 동일 | 409 | `stale_revision`, `idempotency_key_conflict` | 상태 재조회 또는 새 동작 키 생성 |
| 동일 | 422 | `not_initialized`, `item_not_found`, `inventory_full`, `full_board` | 상태·공간 확인 |

생성 실패 DTO는 `{ code, message, board, economy, generator }`, 판매 실패 DTO는
`{ code, message, board, economy }`, 보드 액션·머지 실패 DTO는
`{ code, message, currentRevision, board }`, 보관함 이동 실패 DTO는
`{ code, message, board, inventory }`입니다. 실패하거나 같은 멱등 키가 재생되면 도감 발견이
추가되지 않습니다. MySQL 데드락으로 같은 키의 두 요청 중 하나가 진 경우 `409 stale_revision`이
가능합니다. 결과를 확정하지 못한 네트워크 요청은 **같은 키**로 재시도하면 성공 영수증을 조회합니다.

보호된 게임 API는 미인증 시 401, 정지 계정은 403입니다. 클라이언트 버전이 최소 요구보다 낮으면
미들웨어가 426 ProblemDetails(`code: client_upgrade_required`)를 반환할 수 있습니다.
정확한 DTO와 경로는 버전 관리하는 [OpenAPI v1 스냅샷](openapi-v1.json)을 기준으로 합니다.

스냅샷 변경 시 계약 변경을 리뷰한 뒤 저장소 루트에서 다음 명령으로 갱신합니다.

```powershell
$env:UPDATE_OPENAPI_SNAPSHOT = '1'
dotnet test ProjectMerge.sln --filter FullyQualifiedName~OpenApiJson_MatchesVersionControlledSnapshot
Remove-Item Env:UPDATE_OPENAPI_SNAPSHOT
```

일반 테스트와 CI는 현재 서버가 생성한 OpenAPI와 커밋된 JSON의 구조적 일치를 검사합니다.
