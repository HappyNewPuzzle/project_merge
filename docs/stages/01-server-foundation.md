# 1단계 — 서버 및 MySQL 기반 구성

## 단계 목표

게임 기능을 추가하기 전에 서버가 일관된 .NET 버전에서 빌드되고, MySQL 연결을
애플리케이션의 표준 데이터 접근 계층으로 사용할 수 있는 기반을 만듭니다.

## 구현 내용

### 1. 프로젝트 구조

- `ProjectMerge.sln`: 서버 프로젝트를 묶는 솔루션
- `src/MergeGame.Server`: ASP.NET Core 서버
- `Endpoints`: HTTP 경로와 응답 정의
- `Infrastructure/Persistence`: EF Core와 MySQL 연결 구현

### 2. 서버 엔드포인트

- `GET /`: 서비스 이름과 실행 상태 안내
- `GET /health`: 서버 및 MySQL 연결 상태 확인

MySQL이 정상 연결되면 `/health`는 HTTP 200을 반환합니다. 연결할 수 없으면 HTTP
503을 반환하므로 컨테이너 오케스트레이터나 모니터링 시스템이 비정상 인스턴스를
판별할 수 있습니다.

### 3. MySQL 설정

데이터 공급자는 `Pomelo.EntityFrameworkCore.MySql`을 사용합니다. 서버 버전은
MySQL 8.0.36으로 명시했습니다. 실제 개발·운영 DB도 MySQL 8 계열로 통일합니다.

연결 문자열 설정 키는 다음과 같습니다.

```text
ConnectionStrings:MergeGameDatabase
```

로컬 PowerShell에서는 다음처럼 환경 변수를 설정할 수 있습니다.

```powershell
$env:ConnectionStrings__MergeGameDatabase='Server=localhost;Port=3306;Database=merge_game;User=merge_game_app;Password=실제비밀번호'
dotnet run --project src/MergeGame.Server
```

환경 변수의 `__`는 ASP.NET Core에서 설정 계층 구분자인 `:`로 변환됩니다.
실제 비밀번호는 Git에 커밋하지 않습니다.

### 4. 설계 결정

- **.NET 8 고정:** 장기 지원 버전이며 개발 환경 차이로 인한 빌드 오차를 줄입니다.
- **DI 확장 메서드 분리:** 데이터베이스 등록 세부사항이 서버 진입점을 오염시키지 않습니다.
- **명시적 MySQL 버전:** 시작 시 자동 버전 탐지를 위한 불필요한 연결을 피합니다.
- **시작 시 설정 검증:** 연결 문자열이 없으면 요청 처리 전 즉시 오류 원인을 보여 줍니다.
- **자동 마이그레이션 미적용:** 운영 서버 여러 대가 동시에 스키마를 변경하는 위험을 막기 위해
  마이그레이션 실행은 이후 배포 단계에서 별도 명령으로 관리합니다.

## 실행 및 확인

```powershell
dotnet restore
dotnet build ProjectMerge.sln --no-restore
dotnet run --project src/MergeGame.Server
```

MySQL 실행 전에도 빌드는 확인할 수 있습니다. `/health`의 정상 응답을 확인하려면
설정된 주소에서 MySQL 8 서버가 실행 중이어야 합니다.

## 단계 완료 검증

2026-07-28에 다음 항목을 확인했습니다.

- `dotnet build ProjectMerge.sln --no-restore`: 경고 0개, 오류 0개
- MySQL이 없는 테스트 환경에서 `GET /`: HTTP 200 및 서버 정보 JSON 반환
- 같은 환경에서 `GET /health`: HTTP 503 및 `Unhealthy` 반환

마지막 결과는 오류가 아니라 의도한 동작입니다. 서버 프로세스가 실행 중이어도 필수
저장소인 MySQL을 사용할 수 없으면 트래픽을 받지 않도록 비정상 상태로 보고합니다.
실제 MySQL 연결의 HTTP 200 검증은 DB 인스턴스와 계정이 준비된 환경에서 수행합니다.

## 다음 단계 제안

2단계에서는 플레이어 식별자와 계정 기본 정보를 도메인 모델로 정의하고, 최초 EF
Core 마이그레이션 및 계정 생성 API를 추가합니다.
