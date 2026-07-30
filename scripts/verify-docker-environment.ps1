<#
.SYNOPSIS
    MySQL 컨테이너, EF 마이그레이션, 서버 이미지와 헬스 체크를 순서대로 검증합니다.

.DESCRIPTION
    비밀값은 매 실행마다 메모리에서 생성하고 파일에 기록하지 않습니다.
    기존 MySQL 볼륨은 삭제하지 않으므로 반복 실행해도 저장 데이터가 유지됩니다.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function New-RandomSecret {
    # 48바이트 난수는 MySQL 비밀번호와 JWT HMAC 키에 충분한 엔트로피를 제공합니다.
    $bytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(48)
    return [Convert]::ToBase64String($bytes)
}

$env:MYSQL_ROOT_PASSWORD = New-RandomSecret
$env:MYSQL_APP_PASSWORD = New-RandomSecret
$env:JWT_SIGNING_KEY = New-RandomSecret
$env:ConnectionStrings__MergeGameDatabase = "Server=127.0.0.1;Port=3306;Database=merge_game;User=merge_game_app;Password=$($env:MYSQL_APP_PASSWORD)"
$env:Jwt__SigningKey = $env:JWT_SIGNING_KEY

Write-Host '1/4 MySQL 8.0.36 컨테이너를 시작하고 healthcheck를 기다립니다.'
docker compose up --detach --wait mysql

Write-Host '2/4 저장소 고정 EF 도구를 복원하고 모든 마이그레이션을 적용합니다.'
dotnet tool restore
dotnet tool run dotnet-ef database update `
    --project src/MergeGame.Server `
    --startup-project src/MergeGame.Server

Write-Host '3/4 서버 이미지를 빌드하고 컨테이너를 시작합니다.'
docker compose up --detach --build server

Write-Host '4/4 서버 시작과 MySQL 연결 상태를 확인합니다.'
$deadline = [DateTime]::UtcNow.AddSeconds(45)
do {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8080/health'
        if ($response.StatusCode -eq 200) {
            Write-Host '검증 성공: 서버와 MySQL이 정상 상태입니다.'
            exit 0
        }
    }
    catch {
        # 컨테이너 시작 중의 연결 거부와 503은 제한 시간까지 재시도합니다.
    }
    Start-Sleep -Seconds 1
} while ([DateTime]::UtcNow -lt $deadline)

Write-Error '45초 안에 /health HTTP 200을 확인하지 못했습니다. docker compose logs를 확인하세요.'
