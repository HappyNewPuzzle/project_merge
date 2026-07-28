# 2단계 — 플레이어 모델 및 게스트 계정 생성

## 단계 목표

클라이언트가 별도의 이메일이나 플랫폼 계정 없이 게임을 시작할 수 있도록 게스트
플레이어를 생성하고, 필요한 데이터를 MySQL에 안전하게 저장합니다.

## 구현 구조

```text
HTTP POST /api/v1/players/guest
          ↓
CreateGuestPlayerService
          ↓
Player 도메인 엔티티
          ↓
EF Core → MySQL players 테이블
```

- `Domain/Players/Player.cs`: 플레이어의 필수 값과 생성 규칙
- `Application/Players/CreateGuestPlayerService.cs`: 토큰 생성과 DB 저장 유스케이스
- `Infrastructure/Security`: 암호학적 난수 토큰과 SHA-256 해시 생성
- `Infrastructure/Persistence/Configurations`: MySQL 컬럼과 인덱스 매핑
- `Endpoints/PlayerEndpoints.cs`: HTTP 요청 및 응답 변환

## 게스트 생성 API

### 요청

```http
POST /api/v1/players/guest
Content-Length: 0
```

요청 본문은 없습니다.

### 성공 응답

```http
HTTP/1.1 201 Created
Location: /api/v1/players/5a10cf89-9f74-442d-95f1-f30ca5dc8d87
Content-Type: application/json
```

```json
{
  "playerId": "5a10cf89-9f74-442d-95f1-f30ca5dc8d87",
  "displayName": "Guest-5a10cf89",
  "guestToken": "32바이트-난수로-생성된-URL-safe-토큰",
  "createdAtUtc": "2026-07-28T12:00:00Z"
}
```

`guestToken`은 생성 응답에서만 한 번 제공됩니다. Unity 클라이언트는 플랫폼별 보안
저장소에 토큰을 보관해야 합니다. 일반 로그나 분석 이벤트에는 포함하지 않습니다.

## 보안 설계

1. `RandomNumberGenerator`로 256비트 난수를 생성합니다.
2. HTTP와 JSON에서 안전한 패딩 없는 URL-safe Base64 문자열로 변환합니다.
3. 클라이언트에는 원본 토큰을 한 번 반환합니다.
4. 서버 DB에는 SHA-256 해시만 저장합니다.
5. `guest_token_hash`에 고유 인덱스를 적용해 중복을 차단합니다.

게스트 토큰은 사람이 정하는 짧은 비밀번호가 아니라 충분히 긴 무작위 값이므로
빠른 SHA-256 해시를 사용해도 사전 대입 공격이 현실적으로 어렵습니다.

## MySQL 스키마

최초 마이그레이션은 `players` 테이블을 생성합니다.

| 컬럼 | 형식 | 설명 |
|---|---|---|
| `id` | `char(36)` | 플레이어 GUID 기본 키 |
| `display_name` | `varchar(32)` | 기본 게스트 표시 이름 |
| `guest_token_hash` | `char(64)` | SHA-256 해시, 고유 인덱스 |
| `created_at_utc` | `datetime(6)` | UTC 계정 생성 시각 |

표시 이름은 한글과 이모지를 지원하도록 `utf8mb4`를 사용합니다. 토큰 해시는 ASCII
대문자 16진수의 정확한 비교를 위해 `ascii_bin` collation을 사용합니다.

## 마이그레이션 적용

저장소에 고정된 도구를 복원하고 연결 문자열을 설정한 뒤 적용합니다.

```powershell
dotnet tool restore
$env:ConnectionStrings__MergeGameDatabase='Server=localhost;Port=3306;Database=merge_game;User=merge_game_app;Password=실제비밀번호'
dotnet tool run dotnet-ef database update --project src/MergeGame.Server --startup-project src/MergeGame.Server
```

운영 환경에서는 서버 인스턴스가 시작할 때 자동 적용하지 않습니다. 배포 작업에서
마이그레이션 명령을 한 번만 실행해야 여러 서버가 동시에 스키마를 변경하지 않습니다.

## 자동 테스트

`dotnet test ProjectMerge.sln`은 다음을 검증합니다.

- 게스트 토큰이 256비트 난수 기반의 URL-safe 형식인지
- 토큰 해시가 실제 SHA-256 계산 결과와 일치하는지
- 연속 생성 토큰이 서로 다른지
- 원본 토큰은 응답에만 있고 DB에는 해시만 저장되는지
- 플레이어 이름과 UTC 생성 시각이 올바르게 저장되는지

## 단계 완료 검증

2026-07-28에 다음 항목을 확인했습니다.

- 전체 솔루션 빌드: 경고 0개, 오류 0개
- 자동 테스트: 3개 통과, 실패 0개
- EF Core가 `players` 테이블, 기본 키, 토큰 고유 인덱스를 포함한 MySQL SQL 생성
- 직접 및 전이 NuGet 패키지 취약성 검사: 알려진 취약 패키지 0개
- 실제 MySQL 통합 실행: 로컬 MySQL/Docker 인스턴스가 없어 미수행

`*.Designer.cs`와 모델 스냅샷은 EF Core가 자동 생성하고 다음 마이그레이션의 기준으로
사용하므로 수동 주석을 추가하지 않습니다. 직접 작성한 도메인·서비스·엔드포인트·
보안·매핑·테스트 코드에는 역할과 설계 이유를 설명하는 주석을 포함했습니다.

## 다음 단계 제안

3단계에서는 게스트 토큰 로그인 API, 인증된 플레이어 컨텍스트, 짧은 수명의 JWT
액세스 토큰을 구현하는 것이 적합합니다.
