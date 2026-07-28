# 3단계 — 게스트 로그인 및 JWT 인증

## 단계 목표

2단계에서 발급한 플레이어 ID와 게스트 토큰을 검증해 짧은 수명의 JWT 액세스
토큰을 발급하고, 보호된 API가 인증된 플레이어를 안전하게 식별하도록 구성합니다.

## 인증 흐름

```text
playerId + guestToken
        ↓
SHA-256 해시 및 고정 시간 비교
        ↓
HMAC SHA-256 서명 JWT 발급
        ↓
Authorization: Bearer {JWT}
        ↓
JWT 서명·발급자·대상·만료 검증
        ↓
sub 클레임 → 현재 플레이어 ID
```

원본 게스트 토큰은 로그인 요청에만 사용합니다. 일반 게임 API에는 만료 시간이 짧은
JWT를 사용하므로 원본 자격 증명의 노출 범위를 줄입니다.

## 환경 설정

`appsettings.json`의 `CHANGE_ME`는 실행 가능한 키가 아닙니다. 서버를 실행하기 전에
최소 32바이트의 예측 불가능한 서명 키를 환경 변수로 설정해야 합니다.

```powershell
$jwtBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(48)
$env:Jwt__SigningKey = [Convert]::ToBase64String($jwtBytes)
```

지원하는 JWT 설정은 다음과 같습니다.

| 환경 변수 | 기본값 | 설명 |
|---|---|---|
| `Jwt__Issuer` | `MergeGame.Server` | 토큰 발급 서버 |
| `Jwt__Audience` | `MergeGame.Client` | 토큰 사용 대상 |
| `Jwt__SigningKey` | 없음 | 최소 32바이트 HMAC 비밀 키 |
| `Jwt__AccessTokenMinutes` | `15` | 1~60분 액세스 토큰 수명 |

운영 환경에서는 키를 배포 시스템의 비밀 저장소에서 주입하고 Git, 로그, Unity
클라이언트에 포함하지 않습니다. 여러 서버 인스턴스는 같은 키를 사용해야 서로
발급한 JWT를 검증할 수 있습니다.

## 게스트 로그인 API

### 요청

```http
POST /api/v1/auth/guest
Content-Type: application/json
```

```json
{
  "playerId": "5a10cf89-9f74-442d-95f1-f30ca5dc8d87",
  "guestToken": "게스트-생성-응답에서-받은-원본-토큰"
}
```

### 성공 응답

```http
HTTP/1.1 200 OK
Content-Type: application/json
```

```json
{
  "playerId": "5a10cf89-9f74-442d-95f1-f30ca5dc8d87",
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "tokenType": "Bearer",
  "expiresAtUtc": "2026-07-28T12:15:00Z"
}
```

잘못된 플레이어 ID와 잘못된 토큰은 모두 HTTP 401을 반환합니다. 응답을 구분하지
않아 공격자가 계정 존재 여부를 확인하기 어렵게 합니다.

## 인증된 현재 플레이어 API

```http
GET /api/v1/players/me
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

성공 시 토큰의 `sub`와 일치하는 플레이어의 ID, 표시 이름, 생성 시각을 반환합니다.
게스트 토큰 해시는 응답 모델에 포함하지 않습니다.

- JWT 없음: HTTP 401
- 서명 또는 발급자/대상 오류: HTTP 401
- 만료된 JWT: HTTP 401
- JWT는 유효하지만 플레이어가 삭제됨: HTTP 404

## 보안 설계

- HMAC SHA-256 서명과 최소 32바이트 키를 사용합니다.
- 발급자(`iss`)와 대상(`aud`)을 모두 검증합니다.
- 만료(`exp`)가 없는 토큰은 허용하지 않습니다.
- 서버 간 시각 오차 허용 범위는 30초로 제한합니다.
- 플레이어 ID는 표준 `sub` 클레임에만 저장합니다.
- JWT에는 게스트 원본 토큰이나 해시를 넣지 않습니다.
- 토큰 해시는 `CryptographicOperations.FixedTimeEquals`로 비교합니다.
- 게스트 생성과 로그인은 IP별 분당 10회로 제한하며 초과 시 HTTP 429를 반환합니다.

현재 속도 제한의 IP 기준은 서버가 직접 인터넷 요청을 받는 구성을 가정합니다.
리버스 프록시를 사용할 때는 신뢰할 프록시만 허용하는 Forwarded Headers 설정을
완료한 뒤 실제 클라이언트 IP를 사용해야 합니다.

## 자동 테스트

`dotnet test ProjectMerge.sln`은 기존 테스트와 함께 다음을 검증합니다.

- 올바른 플레이어 ID와 게스트 토큰만 JWT 발급으로 이어지는지
- 잘못된 토큰과 존재하지 않는 플레이어가 동일하게 거부되는지
- JWT의 HMAC 서명, 발급자, 대상이 실제 검증되는지
- JWT `sub`, `jti`, 만료 시각, 서명 알고리즘이 올바른지

## 단계 완료 검증

2026-07-28에 다음 항목을 확인했습니다.

- 전체 솔루션 빌드: 경고 0개, 오류 0개
- 자동 테스트: 7개 통과, 실패 0개
- JWT 없이 `GET /api/v1/players/me` 호출: HTTP 401 및 Bearer challenge 반환
- 서버 루트 API: HTTP 200
- 직접 및 전이 NuGet 패키지 취약성 검사: 알려진 취약 패키지 0개
- 실제 MySQL 로그인 통합 실행: 로컬 MySQL/Docker 인스턴스가 없어 미수행

## 다음 단계 제안

4단계에서는 머지 게임의 핵심인 아이템 정의, 플레이어 보드, 서버 검증 머지 규칙,
동시 요청 방지를 위한 낙관적 잠금을 구현하는 것이 적합합니다.
