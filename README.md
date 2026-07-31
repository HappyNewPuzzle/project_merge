# Project Merge

머지 퍼즐 게임의 서버 프로젝트입니다. 각 개발 단계는 실행 가능한 코드, 상세 주석,
검증 결과, 단계 문서를 함께 커밋합니다.

## 기술 구성

- .NET 8 / ASP.NET Core
- Entity Framework Core
- MySQL 8

## 현재 단계

- [1단계: 서버 및 MySQL 기반 구성](docs/stages/01-server-foundation.md)
- [2단계: 플레이어 모델 및 게스트 계정 생성](docs/stages/02-guest-player.md)
- [3단계: 게스트 로그인 및 JWT 인증](docs/stages/03-guest-authentication.md)
- [4단계: 플레이어 보드 및 서버 검증 머지](docs/stages/04-board-and-merge.md)
- [5단계: 에너지·재화·아이템 생성기](docs/stages/05-economy-and-generator.md)
- [6단계: 퀘스트·머지 이벤트·멱등 보상](docs/stages/06-quests-and-idempotent-rewards.md)
- [7단계: 관측성 및 Docker 통합 환경](docs/stages/07-observability-and-docker.md)
- [8단계: OpenAPI 계약 및 Unity 클라이언트](docs/stages/08-openapi-and-unity-client.md)
- [9단계: 친구 관계 및 일일 에너지 선물](docs/stages/09-social-friends-and-energy-gifts.md)

## 빠른 실행

1. MySQL 8에 `merge_game` 데이터베이스와 애플리케이션 계정을 만듭니다.
2. `ConnectionStrings__MergeGameDatabase` 환경 변수에 실제 연결 정보를 설정합니다.
3. `Jwt__SigningKey` 환경 변수에 32바이트 이상의 안전한 난수 키를 설정합니다.
4. `dotnet restore` 후 `dotnet run --project src/MergeGame.Server`를 실행합니다.
5. 브라우저 또는 API 도구에서 `/`와 `/health`를 확인합니다.

## 게스트 계정 생성

MySQL 스키마를 적용한 뒤 다음 API로 새 플레이어를 생성합니다.

```powershell
curl.exe -X POST https://localhost:7001/api/v1/players/guest
```

응답의 `guestToken`은 원문을 다시 조회할 수 없으므로 클라이언트 보안 저장소에
즉시 보관해야 합니다.

## 로그인 및 인증 API

- `POST /api/v1/auth/guest`: 플레이어 ID와 게스트 토큰으로 JWT 발급
- `GET /api/v1/players/me`: `Authorization: Bearer {JWT}`로 현재 플레이어 조회

자세한 요청과 응답 형식은 [3단계 문서](docs/stages/03-guest-authentication.md)를
참고하세요.

## 소셜 API

- `POST /api/v1/social/profile`: 친구 코드 최초 생성
- `GET /api/v1/social/profile`: 내 친구 코드와 친구 목록 조회
- `POST /api/v1/social/friends`: 친구 코드로 친구 추가
- `POST /api/v1/social/friends/{friendPlayerId}/energy-gift`: 하루 한 번 에너지 5 선물

친구 관계의 중복 방지와 UTC 날짜별 선물 규칙은
[9단계 문서](docs/stages/09-social-friends-and-energy-gifts.md)를 참고하세요.

## 머지 보드 API

- `POST /api/v1/board/`: 인증 플레이어의 5×7 보드 최초 생성
- `GET /api/v1/board/`: 현재 보드와 revision 조회
- `POST /api/v1/board/merge`: 두 슬롯의 서버 검증 머지

모든 보드 API는 Bearer JWT가 필요합니다. 변경 요청에는 마지막으로 받은 `revision`을
보내야 하며, 자세한 규칙은 [4단계 문서](docs/stages/04-board-and-merge.md)에 있습니다.

## 경제 API

- `POST /api/v1/economy/`: 최대 에너지 100, 코인 0으로 최초 초기화
- `GET /api/v1/economy/`: 서버 시간 기준 에너지와 코인 조회
- `POST /api/v1/economy/generate`: 에너지 1을 소비해 빈 슬롯에 아이템 생성
- `POST /api/v1/economy/daily-reward`: UTC 날짜 기준 하루 한 번 코인 50 지급

자세한 규칙은 [5단계 문서](docs/stages/05-economy-and-generator.md)에 있습니다.

## 퀘스트 API

- `POST /api/v1/quests/`: 첫 머지 퀘스트 초기화
- `GET /api/v1/quests/`: 현재 진행도 조회
- `POST /api/v1/quests/{questId}/claim`: 멱등성 키로 보상 수령

상세 흐름은 [6단계 문서](docs/stages/06-quests-and-idempotent-rewards.md)에 있습니다.

## Docker 통합 환경

Docker가 실행 중인 개발 PC에서는 다음 명령으로 MySQL, 마이그레이션, 서버 빌드,
헬스 체크를 순서대로 검증할 수 있습니다.

```powershell
.\scripts\verify-docker-environment.ps1
```

운영 로그와 trace ID 규칙은 [7단계 문서](docs/stages/07-observability-and-docker.md)를 참고하세요.

## API 문서와 Unity 연동

- OpenAPI JSON: `/swagger/v1/swagger.json`
- Swagger UI: `/docs`
- Unity 클라이언트: [`clients/unity`](clients/unity)

서버 실행 후 Swagger UI에서 요청·응답 형식과 Bearer 인증을 시험할 수 있습니다.
Unity 적용 및 revision 충돌 처리 방법은 [8단계 문서](docs/stages/08-openapi-and-unity-client.md)를
참고하세요.

> 저장소의 기본 연결 문자열에 있는 `CHANGE_ME`는 문서용 값입니다.
> 실제 비밀번호를 `appsettings*.json`이나 Git 커밋에 포함하지 마세요.
