using MergeGame.Server.Domain.Content;
using MergeGame.Server.Infrastructure.Compatibility;
using MergeGame.Server.Infrastructure.OpenApi;

namespace MergeGame.Server.Endpoints;

/// <summary>Unity가 로그인 전에 서버 호환성을 확인하는 공개 버전 계약입니다.</summary>
public static class VersionEndpoints
{
    public static WebApplication MapVersionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/version", (ClientCompatibilityOptions options) => Results.Ok(
            new ServerVersionResponse(
                options.ServerVersion,
                ApiContract.Version,
                GameContentVersion.Current,
                options.MinimumClientVersion,
                options.RequireVersionHeader)))
            .WithTags("Version")
            .WithName("GetServerVersion")
            .Produces<ServerVersionResponse>();
        return app;
    }
}

public sealed record ServerVersionResponse(
    string ServerVersion,
    string ApiVersion,
    string ContentVersion,
    string MinimumClientVersion,
    bool ClientVersionHeaderRequired);
