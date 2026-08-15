<#
.SYNOPSIS
    배포 후보의 Release 빌드, 테스트, EF 모델 동기화와 publish를 순서대로 검증합니다.
.DESCRIPTION
    ConnectionStrings__MergeGameDatabase와 Jwt__SigningKey는 호출 환경의 비밀 저장소에서
    미리 주입해야 하며 이 스크립트는 비밀값을 파일이나 출력에 기록하지 않습니다.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__MergeGameDatabase)) {
    throw 'ConnectionStrings__MergeGameDatabase 환경 변수가 필요합니다.'
}
if ([string]::IsNullOrWhiteSpace($env:Jwt__SigningKey) -or $env:Jwt__SigningKey.Length -lt 32) {
    throw 'Jwt__SigningKey에는 32자 이상의 실제 비밀 키가 필요합니다.'
}

dotnet tool restore
dotnet restore ProjectMerge.sln --locked-mode
dotnet build ProjectMerge.sln --configuration Release --no-restore
dotnet test ProjectMerge.sln --configuration Release --no-build --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes `
    --project src/MergeGame.Server `
    --startup-project src/MergeGame.Server `
    --configuration Release `
    --no-build
dotnet publish src/MergeGame.Server --configuration Release --no-restore --output artifacts/server

Write-Host 'Release 후보 검증에 성공했습니다. DB 마이그레이션은 승인된 배포 단계에서 별도로 적용하세요.'
