# 빌드 단계는 저장소의 global.json과 같은 .NET 8 SDK 패치 버전을 사용합니다.
FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src

# 프로젝트 파일을 먼저 복사하면 소스만 바뀐 빌드에서 NuGet 복원 계층을 재사용할 수 있습니다.
COPY global.json ProjectMerge.sln ./
COPY src/MergeGame.Server/MergeGame.Server.csproj src/MergeGame.Server/
RUN dotnet restore src/MergeGame.Server/MergeGame.Server.csproj

COPY src/MergeGame.Server/ src/MergeGame.Server/
RUN dotnet publish src/MergeGame.Server/MergeGame.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# 실행 이미지는 SDK와 소스 코드를 제외한 ASP.NET Core 런타임만 포함합니다.
FROM mcr.microsoft.com/dotnet/aspnet:8.0.29 AS runtime
WORKDIR /app

# 권한이 제한된 기본 .NET 사용자로 실행해 컨테이너 침해 시 피해 범위를 줄입니다.
USER app
COPY --from=build --chown=app:app /app/publish ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MergeGame.Server.dll"]
