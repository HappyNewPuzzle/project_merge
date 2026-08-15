using MergeGame.Server.Application.Game;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>Unity의 게임 진입과 전체 상태 동기화를 위한 API를 등록합니다.</summary>
public static class GameEndpoints
{
    public static WebApplication MapGameEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/game").WithTags("Game").RequireAuthorization();
        group.MapPost("/bootstrap", BootstrapAsync).WithName("BootstrapGame")
            .Produces<GameBootstrapResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    /// <summary>누락된 플레이어 하위 상태를 초기화하고 플레이에 필요한 전체 스냅샷을 반환합니다.</summary>
    private static async Task<IResult> BootstrapAsync(
        ICurrentPlayerAccessor currentPlayer,
        GameBootstrapService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
            return Results.Unauthorized();

        var response = await service.ExecuteAsync(playerId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }
}
