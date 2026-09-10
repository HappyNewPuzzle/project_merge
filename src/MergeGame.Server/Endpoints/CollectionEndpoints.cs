using MergeGame.Server.Application.Boards;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Endpoints;

/// <summary>계정 ID를 클라이언트에서 받지 않는 읽기 전용 도감 API입니다.</summary>
public static class CollectionEndpoints
{
    public static WebApplication MapCollectionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/collection", async (
            ICurrentPlayerAccessor accessor, MergeGameDbContext db, CancellationToken token) =>
        {
            if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
            if (!await db.Players.AnyAsync(x => x.Id == playerId, token)) return Results.NotFound();
            return Results.Ok(await ItemCollection.ReadAsync(db, playerId, token));
        }).RequireAuthorization().WithTags("Collection").WithName("GetCollection")
            .Produces<CollectionState>(200).Produces(401).Produces(404);
        return app;
    }
}
