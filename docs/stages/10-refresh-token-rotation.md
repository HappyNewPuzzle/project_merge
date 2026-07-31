# 10단계 — Refresh token 회전 및 로그아웃

## 단계 목표

15분 액세스 JWT가 만료될 때 게스트 원본 자격 증명을 반복 전송하지 않고 로그인 상태를
안전하게 갱신합니다. refresh token은 사용할 때마다 새 값으로 회전하며, 이미 사용한
토큰이 다시 제출되면 탈취 가능성으로 보고 같은 토큰 계열을 전부 폐기합니다.

## 토큰 수명과 저장

- access token: 기본 15분, 서버가 DB 조회 없이 JWT 서명과 만료 검증
- refresh token: 기본 30일, 설정 허용 범위 1~90일
- 원문 엔트로피: `RandomNumberGenerator`가 생성한 256비트
- DB 저장: 원문이 아닌 SHA-256 해시만 저장
- 클라이언트 저장: Android Keystore, iOS Keychain 등 플랫폼 보안 저장소

`Jwt:RefreshTokenDays` 환경 설정으로 기간을 조정할 수 있습니다. 로그, URL 쿼리,
PlayerPrefs, 분석 이벤트에는 어떤 토큰도 기록하지 않습니다.

## 로그인 응답

`POST /api/v1/auth/guest` 성공 응답에 기존 필드와 함께 다음 값이 추가됩니다.

```json
{
  "accessToken": "...",
  "expiresAtUtc": "2026-07-31T15:15:00Z",
  "refreshToken": "...",
  "refreshTokenExpiresAtUtc": "2026-08-30T15:00:00Z"
}
```

refresh token 원문은 이 응답에서만 확인할 수 있습니다.

## 회전 API

`POST /api/v1/auth/refresh`는 JWT 없이 호출하며 인증 속도 제한 정책을 적용합니다.

```json
{ "refreshToken": "현재 보관 중인 원문" }
```

성공하면 기존 refresh session을 `rotated`로 폐기하고, 같은 family ID를 가진 새 session과
새 JWT를 반환합니다. 기존 토큰 폐기와 새 토큰 저장은 하나의 `SaveChanges` 트랜잭션으로
처리됩니다. 클라이언트는 성공 응답을 받은 즉시 두 토큰을 모두 교체해야 합니다.

만료됐거나 알 수 없는 토큰은 HTTP 401입니다. 이미 회전된 토큰을 다시 사용하면
`reuse_detected`로 해당 family의 활성 session까지 폐기하므로 정상 기기도 게스트
자격 증명으로 다시 로그인해야 합니다. 이는 복제된 토큰의 지속 사용을 차단합니다.

## 로그아웃

`POST /api/v1/auth/logout`은 유효한 Bearer JWT와 refresh token 본문을 함께 요구합니다.
토큰이 현재 플레이어 소유일 때 `logout` 사유로 폐기합니다. 이미 폐기됐거나 존재하지
않는 값을 다시 보내도 HTTP 204로 처리해 로그아웃을 멱등 동작으로 유지합니다.

현재 발급된 JWT는 자체 포함 토큰이므로 로그아웃 직후에도 최대 남은 15분 동안 유효할
수 있습니다. 즉시 JWT 폐기가 필요한 운영 요구가 생기면 jti 차단 목록을 별도 단계로
도입해야 합니다.

## MySQL 모델

`AddRefreshTokenRotation` 마이그레이션은 `refresh_token_sessions` 테이블을 추가합니다.

- `token_hash` 고유 인덱스: 같은 원문 저장 차단과 빠른 교환 조회
- `(player_id, family_id)` 인덱스: 재사용 탐지 시 계열 전체 폐기
- `revoked_at_utc` 동시성 토큰: 동일 refresh token의 병렬 회전 중 하나만 성공
- `replaced_by_session_id`: 회전 계보 추적
- `revocation_reason`: `rotated`, `logout`, `reuse_detected`
- 플레이어 삭제 시 session CASCADE 삭제

## Unity 처리 순서

Unity 클라이언트에 `RefreshTokenRequest`, 새 로그인 응답 필드,
`RefreshAccessToken`, `Logout` 코루틴을 추가했습니다.

1. 로그인 성공 시 access/refresh token을 함께 안전하게 저장합니다.
2. access token 만료 직전 또는 보호 API 401 후 refresh를 한 번 호출합니다.
3. 성공 시 두 토큰을 원자적으로 교체하고 원래 API를 한 번만 재시도합니다.
4. refresh도 401이면 저장 토큰을 삭제하고 게스트 로그인을 다시 수행합니다.
5. 여러 요청이 동시에 401을 받아도 refresh 호출은 클라이언트에서 하나로 직렬화합니다.

마지막 규칙을 지키지 않으면 같은 refresh token의 병렬 사용이 재사용 공격처럼 감지될
수 있습니다.

## 단계 완료 검증

2026-07-31에 다음 항목을 확인했습니다.

- Release 빌드: 경고 0개, 오류 0개
- 자동 테스트: 33개 통과, 실패 0개, 건너뜀 0개
- 정상 회전 후 이전 토큰 재사용 시 replacement session까지 폐기되는 테스트 통과
- OpenAPI v1 핵심 경로 15개 및 refresh/logout 계약 검사 통과
- EF Core 모델 변경 미반영 여부: 없음
- MySQL 마이그레이션 SQL의 session 테이블, token hash 고유 인덱스, family 인덱스 확인
- 알려진 NuGet 취약 패키지: 직접 및 전이 패키지 모두 0개

## 다음 단계 제안

11단계에서는 만료 session 정리 백그라운드 작업과 동시 refresh 요청 단일화 통합
테스트를 추가하거나, 운영자용 플레이어 조회·제재·감사 API를 구축할 수 있습니다.
