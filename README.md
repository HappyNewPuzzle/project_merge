# Project Merge

머지 퍼즐 게임의 서버 프로젝트입니다. 각 개발 단계는 실행 가능한 코드, 상세 주석,
검증 결과, 단계 문서를 함께 커밋합니다.

## 기술 구성

- .NET 8 / ASP.NET Core
- Entity Framework Core
- MySQL 8

## 현재 단계

- [1단계: 서버 및 MySQL 기반 구성](docs/stages/01-server-foundation.md)

## 빠른 실행

1. MySQL 8에 `merge_game` 데이터베이스와 애플리케이션 계정을 만듭니다.
2. `ConnectionStrings__MergeGameDatabase` 환경 변수에 실제 연결 정보를 설정합니다.
3. `dotnet restore` 후 `dotnet run --project src/MergeGame.Server`를 실행합니다.
4. 브라우저 또는 API 도구에서 `/`와 `/health`를 확인합니다.

> 저장소의 기본 연결 문자열에 있는 `CHANGE_ME`는 문서용 값입니다.
> 실제 비밀번호를 `appsettings*.json`이나 Git 커밋에 포함하지 마세요.
