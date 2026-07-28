namespace MergeGame.Server.Endpoints;

/// <summary>
/// 서버 공통 엔드포인트를 등록하는 확장 메서드를 제공합니다.
/// 기능별 엔드포인트가 늘어나면 이 클래스가 각 기능의 매핑 메서드를 호출하는 진입점이 됩니다.
/// </summary>
public static class ServerEndpointExtensions
{
    /// <summary>
    /// 현재 단계에서 제공하는 서버 상태 확인 API를 등록합니다.
    /// </summary>
    /// <param name="app">요청 경로와 처리기를 연결할 웹 애플리케이션입니다.</param>
    /// <returns>추가 설정을 이어갈 수 있도록 입력받은 애플리케이션을 반환합니다.</returns>
    public static WebApplication MapServerEndpoints(this WebApplication app)
    {
        // /health는 ASP.NET Core 표준 헬스 체크 결과를 반환합니다.
        // 등록된 검사 중 하나라도 실패하면 HTTP 503을 반환하므로 배포 상태 판정에 사용할 수 있습니다.
        app.MapHealthChecks("/health");

        // 루트 경로는 사람이 브라우저에서 서버 실행 여부를 빠르게 확인하기 위한 간단한 안내 API입니다.
        app.MapGet("/", () => Results.Ok(new ServerInformationResponse(
            Service: "MergeGame.Server",
            Status: "running",
            HealthEndpoint: "/health")));

        return app;
    }
}

/// <summary>
/// 루트 경로에서 반환하는 서버 기본 정보입니다.
/// record를 사용하면 응답 전용 불변 데이터 구조를 간결하고 명확하게 표현할 수 있습니다.
/// </summary>
/// <param name="Service">실행 중인 서비스의 이름입니다.</param>
/// <param name="Status">현재 서버 프로세스의 상태입니다.</param>
/// <param name="HealthEndpoint">상세 상태를 확인할 수 있는 상대 경로입니다.</param>
public sealed record ServerInformationResponse(
    string Service,
    string Status,
    string HealthEndpoint);
