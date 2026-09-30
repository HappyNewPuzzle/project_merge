# SV-027 — API Initialization Contract Cleanup

## 결정 및 클라이언트 계약

- 게스트 생성은 `POST /api/v1/players/guest`를 요청 본문 없이 호출합니다. 성공 시 `201 Created`와 기존 JSON `{ playerId, displayName, guestToken, createdAtUtc }`를 반환합니다. 존재하지 않는 `GET /api/v1/players/{playerId}`를 가리키던 `Location` 헤더는 더 이상 보내지 않습니다. 새 플레이어 조회 API는 추가하지 않았습니다.
- 인증 후 게임 상태의 정식 초기화 경로는 `POST /api/v1/game/bootstrap`입니다. 신규 Unity 클라이언트는 이 경로만 사용하세요.
- `POST /api/v1/board/`는 기존 클라이언트 호환을 위해 계속 동작하지만 OpenAPI에서 `deprecated: true`로 표시합니다. 이 경로의 요청·응답이나 인증 정책은 변경하지 않았습니다. 실제 제거는 클라이언트 호출량 0을 확인한 후 별도 작업에서 결정합니다.
- API 버전은 v1 그대로입니다. DB 스키마와 마이그레이션 변경은 없습니다.

## 구현 및 검증

게스트 응답을 201 JSON으로 명시해 잘못된 `Location` 생성을 중단했습니다. 보드 초기화 라우트에 호환성 메타데이터를 표시하고 기존 OpenAPI 생성 필터가 이를 `deprecated` 값으로 내보내도록 했습니다. HTTP 계약 테스트는 게스트 응답 상태·헤더·본문 필드를 확인하고 OpenAPI 계약 테스트는 보드 초기화만 deprecated인지 확인합니다. 변경된 OpenAPI JSON은 `docs/contracts/openapi-v1.json` 스냅샷으로 고정합니다.

전체 회귀 테스트와 CI 결과는 SV-027 완료 보고서를 참조하세요.
