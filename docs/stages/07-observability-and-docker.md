# 7단계 — 관측성 및 Docker 통합 환경

## 단계 목표

운영 장애를 요청 단위로 추적하고 오류 응답 형식을 통일합니다. 개발 환경에서는
MySQL 버전과 .NET 런타임을 Docker로 고정해 마이그레이션부터 헬스 체크까지 같은
절차로 재현합니다.

## 요청 추적 ID

모든 응답에는 `X-Trace-Id`가 포함됩니다. 클라이언트가 8~64자의 영문·숫자·하이픈
값을 보내면 유지하고, 없거나 제어문자 등 안전하지 않은 값이면 서버가 새 값을
생성합니다.

```http
X-Trace-Id: unity-session-1234
```

같은 값이 구조화 로그의 `TraceId`와 오류 응답의 `traceId`에 들어가므로 고객 문의와
서버 로그를 연결할 수 있습니다.

## 표준 오류 응답

처리되지 않은 예외는 RFC 7807 `application/problem+json` HTTP 500으로 변환됩니다.

```json
{
  "type": "https://httpstatuses.com/500",
  "title": "서버 내부 오류가 발생했습니다.",
  "status": 500,
  "detail": "잠시 후 다시 시도하고, 문제가 지속되면 traceId를 전달해 주세요.",
  "instance": "/api/v1/board/merge",
  "code": "internal_server_error",
  "traceId": "unity-session-1234"
}
```

스택 추적, SQL, 연결 문자열, 내부 예외 메시지는 응답에 노출하지 않고 서버 로그에만
남깁니다. 프레임워크가 생성하는 다른 ProblemDetails에도 `traceId`와 요청 경로를
자동 추가합니다.

## 구조화 감사 로그

POST, PUT, PATCH, DELETE 요청은 다음 필드를 기록합니다.

- `TraceId`
- 인증된 `PlayerId` 또는 `anonymous`
- HTTP 메서드와 경로
- 상태 코드
- 처리 시간(ms)

Authorization 헤더, JWT, 게스트 토큰, 쿼리 문자열, 요청·응답 본문은 기록하지
않습니다. 로그 수집 시스템에서는 `TraceId`, `PlayerId`, `StatusCode`를 필드로
파싱해 검색합니다.

## Docker 구성

- 빌드: `.NET SDK 8.0.422`
- 실행: `ASP.NET Core Runtime 8.0.29`
- DB: `MySQL 8.0.36`
- 서버 포트: `8080`
- MySQL 포트: `3306`
- 데이터: 이름 있는 `mergegame-mysql-data` 볼륨

서버 컨테이너는 root가 아닌 .NET 기본 `app` 사용자로 실행합니다. Compose의
비밀번호는 환경 변수에서만 받고 YAML이나 이미지에 저장하지 않습니다.

## 자동 통합 검증

```powershell
.\scripts\verify-docker-environment.ps1
```

스크립트는 다음을 수행합니다.

1. 메모리에서 MySQL/JWT 난수 비밀값 생성
2. MySQL 컨테이너 시작 및 healthcheck 대기
3. 모든 EF Core 마이그레이션 적용
4. 서버 이미지 빌드와 컨테이너 시작
5. `/health`가 HTTP 200인지 최대 45초 확인

스크립트는 DB 볼륨을 삭제하지 않습니다. 종료는 `docker compose down`으로 하며,
데이터를 실제로 삭제해야 할 때만 사용자가 명시적으로 볼륨 삭제를 수행합니다.

수동 실행 시 `.env.docker.example`을 참고하되 예시 값을 그대로 사용하지 않습니다.

## 단계 완료 검증

2026-07-30에 다음 항목을 확인했습니다.

- 전체 솔루션 빌드: 경고 0개, 오류 0개
- 자동 테스트: 24개 통과, 실패 0개, 건너뜀 0개
- 안전한/잘못된 trace ID와 500 ProblemDetails 테스트
- Compose 구문: 필수 환경 변수를 주입한 `docker compose config --quiet` 통과
- 알려진 NuGet 취약 패키지: 직접 및 전이 패키지 모두 0개
- 실제 컨테이너 통합 실행: Docker CLI 29.6.1은 설치되어 있으나 로컬 Docker
  데몬이 실행 중이지 않아 이번 단계에서는 수행하지 못했습니다. 데몬을 실행한 뒤
  `scripts/verify-docker-environment.ps1`로 동일한 검증을 이어갈 수 있습니다.

## 다음 단계 제안

8단계에서는 OpenAPI 문서, API 버전 계약, Unity C# DTO와 클라이언트 SDK 생성을
구축하는 것이 적합합니다.
