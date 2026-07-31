# 8단계 — OpenAPI 계약 및 Unity 클라이언트

## 단계 목표

서버의 v1 HTTP 계약을 기계가 읽을 수 있는 OpenAPI 문서로 공개하고, Unity가 같은
계약을 사용하는 DTO와 코루틴 클라이언트를 제공합니다. 서버 경로, 인증 방식 또는
핵심 엔드포인트가 실수로 바뀌면 통합 테스트가 이를 감지합니다.

## API 버전 계약

현재 안정 버전은 URL 경로의 `/api/v1`입니다. v1을 사용하는 출시 클라이언트가 있는
동안 기존 필드 삭제, 의미 변경, 경로 변경은 하지 않습니다. 호환되지 않는 변경은
향후 `/api/v2`로 추가하고 v1과 일정 기간 함께 운영합니다.

`ApiContract`는 현재 버전, 라우트 접두사, OpenAPI 문서 이름을 한곳에 정의합니다.
OpenAPI JSON 주소는 `/swagger/v1/swagger.json`, 사람이 보는 Swagger UI는 `/docs`입니다.

## OpenAPI와 인증

Swagger 문서는 서버의 실제 Minimal API 메타데이터와 XML 코드 주석에서 생성됩니다.
보호된 엔드포인트에는 HTTP Bearer/JWT 요구사항이 표시됩니다. `/docs`의 Authorize에
로그인 응답의 `accessToken`만 입력하면 UI가 `Authorization: Bearer ...` 헤더를
구성합니다.

문서 공개는 비밀정보 공개가 아닙니다. 연결 문자열, JWT 서명 키, 실제 토큰과 요청
로그는 OpenAPI에 포함하지 않습니다. 운영 환경에서 `/docs` 접근을 제한해야 한다면
리버스 프록시의 사내망 또는 인증 정책을 추가할 수 있습니다.

## Unity 클라이언트

`clients/unity/Runtime`의 두 C# 파일을 Unity 프로젝트
`Assets/MergeGame/Runtime` 아래로 복사합니다.

- `MergeGameApiModels.cs`: 서버 JSON과 camelCase 이름을 맞춘 직렬화 DTO
- `MergeGameApiClient.cs`: `UnityWebRequest` 기반의 모든 v1 게임 API 코루틴
- 외부 JSON 라이브러리 없이 Unity 기본 `JsonUtility` 사용
- 보호 요청에 JWT 자동 추가
- 모든 요청에 문의 추적용 `X-Trace-Id` 추가
- 성공 데이터와 HTTP 상태, ProblemDetails, 원문을 `ApiResult<T>`로 반환

날짜 필드는 Unity 런타임별 `DateTime` 파싱 차이를 피하기 위해 ISO 8601 문자열로
받습니다. 화면에 표시하거나 계산할 때 `DateTimeOffset.TryParse`로 명시적으로
변환합니다.

## 클라이언트 상태 동기화 규칙

보드, 경제, 퀘스트의 `revision`은 단순 표시값이 아니라 동시성 토큰입니다.

1. 조회 또는 변경 성공 응답의 최신 revision을 로컬 상태에 저장합니다.
2. 다음 변경 요청의 `expected...Revision`에 그 값을 보냅니다.
3. HTTP 409를 받으면 관련 상태를 다시 조회합니다.
4. 새 상태에서 사용자 동작이 여전히 유효한 경우에만 사용자의 확인 또는 정해진
   재시도 정책에 따라 다시 요청합니다.

퀘스트 보상 `idempotencyKey`는 한 번의 사용자 보상 동작 동안 유지해야 합니다.
네트워크 타임아웃 뒤 같은 키로 재시도하면 서버가 보상을 중복 지급하지 않습니다.

## 자격 증명 보관

`guestToken`과 `accessToken`을 로그나 `PlayerPrefs` 평문에 저장하지 않습니다. Android
Keystore, iOS Keychain 등 플랫폼 보안 저장소를 사용합니다. 오류 문의에는 토큰 대신
`ApiProblem.traceId`만 전달합니다.

## 단계 완료 검증

2026-07-31에 다음 항목을 확인했습니다.

- 전체 솔루션 빌드: 경고 0개, 오류 0개
- 자동 테스트: 26개 통과, 실패 0개, 건너뜀 0개
- 실제 TestServer의 OpenAPI JSON 응답: HTTP 200
- v1 핵심 게임 경로 10개 계약 검사 통과
- `BoardState` 등 성공 응답 DTO 스키마 참조 검사 통과
- 보호 API의 Bearer/JWT 및 공개 API의 무인증 계약 검사 통과
- 알려진 NuGet 취약 패키지: 직접 및 전이 패키지 모두 0개

## 다음 단계 제안

9단계에서는 친구 코드, 친구 목록과 선물 에너지처럼 플레이어 간 소셜 기능을
추가하거나, 실제 Unity 프로젝트에서 이 클라이언트를 연결하는 플레이 모드 테스트를
구축하는 것이 적합합니다.
