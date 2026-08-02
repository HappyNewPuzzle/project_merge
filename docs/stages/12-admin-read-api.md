# 12단계 — 관리자 인증 및 읽기 전용 운영 API

## 단계 목표

운영자가 장애와 고객 문의를 조사할 때 MySQL에 직접 접속하거나 플레이어 인증 정보를
열람하지 않고 필요한 상태를 확인할 수 있게 합니다. 플레이어 JWT와 분리된 관리자
API 키 인증을 사용하고, 첫 관리자 단계는 데이터 변경 위험이 없는 읽기 전용 API로
제한합니다.

## 기본 비활성화

저장소 기본 설정은 다음과 같습니다.

```json
"AdminApi": {
  "Enabled": false,
  "ApiKey": "",
  "OperatorId": "operations"
}
```

비활성 상태에서는 올바른 형식의 헤더를 보내도 관리자 API가 HTTP 401을 반환합니다.
운영 환경에서만 비밀 저장소를 통해 다음 환경 변수를 주입합니다.

```text
AdminApi__Enabled=true
AdminApi__ApiKey=<32바이트 이상의 암호학적 난수>
AdminApi__OperatorId=live-operations
```

활성화하면서 키가 32바이트보다 짧거나 `CHANGE_ME` 예시 값이면 서버 시작을 거부합니다.
키를 appsettings, Docker 이미지, Git, Unity 프로젝트에 저장하지 않습니다.

## 별도 인증 스키마

관리자 요청은 `X-Admin-Key` 헤더를 사용합니다. 서버는 제출값과 설정값을 각각 SHA-256
처리한 뒤 고정 시간 비교합니다. 성공한 인증 주체에는 설정된 OperatorId와
`Administrator` 역할만 부여합니다.

플레이어 Bearer JWT로는 관리자 API에 접근할 수 없고 관리자 키로는 플레이어 API에
접근할 수 없습니다. OpenAPI도 두 보안 스키마를 구분합니다.

- 게임 API: `Bearer`
- 관리자 API: `AdminApiKey` / `X-Admin-Key`

관리자 요청은 IP별 분당 60회 속도 제한을 적용합니다. 운영 리버스 프록시에서도 VPN,
사내망 또는 허용 IP 목록으로 `/api/v1/admin` 접근을 추가 제한해야 합니다.

## 서버 현황 API

`GET /api/v1/admin/overview`는 다음 집계만 반환합니다.

- 전체 플레이어 수
- 현재 활성 refresh session 수
- 친구 관계 수
- 오늘 UTC 기준 전송된 에너지 선물 수
- 집계 기준 서버 UTC 시각

이 API는 비밀값, 토큰, 개인별 재화 내역을 반환하지 않습니다.

## 플레이어 진단 API

`GET /api/v1/admin/players/{playerId}`는 고객 문의에 필요한 다음 상태를 반환합니다.

- 플레이어 ID, 표시 이름, 가입 UTC 시각
- 현재 에너지, 코인, 경제 revision
- 보드 revision과 아이템 개수
- 친구 수
- 활성 refresh session 개수

guestToken 해시, refresh token 해시, JWT, 친구 코드, 연결 문자열은 DTO에 포함하지
않습니다. 존재하지 않는 플레이어는 HTTP 404를 반환합니다.

## 감사 로그

조회할 때 OperatorId, 대상 PlayerId, 조회 성공 여부를 구조화 로그로 남깁니다. 관리자
키와 응답 본문은 기록하지 않습니다. 7단계의 trace ID가 함께 적용되므로 운영자 작업과
요청 로그를 연결할 수 있습니다.

읽기 권한도 실제 담당자별 추적이 필요하다면 운영 배포에서 공용 키 대신 OperatorId별
키를 가진 별도 인스턴스 또는 향후 OIDC 관리자 로그인을 사용해야 합니다.

## API 호출 예시

```powershell
$headers = @{ "X-Admin-Key" = $env:ADMIN_API_KEY }
Invoke-RestMethod -Uri "https://server/api/v1/admin/overview" -Headers $headers
```

명령 기록에 키 원문이 남지 않도록 환경 변수에서 헤더로 전달합니다. Swagger UI의
AdminApiKey Authorize 기능은 로컬 또는 접근 제한된 운영 도구 환경에서만 사용합니다.

## 데이터베이스 변경

관리자 조회는 기존 테이블과 인덱스를 읽기만 하므로 새 마이그레이션이 없습니다.
각 쿼리는 `AsNoTracking` 또는 집계 SQL을 사용해 변경 추적 비용을 줄입니다.

## 단계 완료 검증

2026-08-03에 다음 항목을 확인했습니다.

- Release 빌드: 경고 0개, 오류 0개
- 자동 테스트: 38개 통과, 실패 0개, 건너뜀 0개
- 정확한 관리자 키 일치·오류·누락 검증 테스트 통과
- 플레이어 요약 집계 및 Token 이름의 비밀 DTO 필드 부재 테스트 통과
- 기본 비활성 관리자 API의 실제 HTTP 401 통합 테스트 통과
- OpenAPI의 AdminApiKey 헤더와 Bearer 분리 계약 테스트 통과
- EF Core 모델 변경 미반영 여부: 없음
- 신규 NuGet 패키지: 0개
- 외부 NuGet 최신 취약성 조회: 11단계와 같은 보안 정책으로 미실행. 패키지 집합은
  취약 패키지 0개로 확인한 10단계와 동일함

## 다음 단계 제안

13단계에서는 관리자별 OIDC 로그인 또는 서명된 관리자 JWT를 도입하고, 계정 정지와
재화 조정 같은 쓰기 작업에 이유·멱등성 키·변경 전후 값을 저장하는 영구 감사 로그를
추가하는 것이 적합합니다.
