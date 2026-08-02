using MergeGame.Server.Domain.Authentication;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Infrastructure.Authentication;

/// <summary>보존 경계와 실행당 삭제 상한이 운영 설정대로 적용되는지 검증합니다.</summary>
public sealed class RefreshSessionCleanupServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_DeletesOnlyExpiredOrRevokedBeyondRetention()
    {
        await using var db = CreateContext();
        var player = await AddPlayerAsync(db);
        var oldExpired = Session(player.Id, "A", Now.UtcDateTime.AddDays(-40));
        var active = Session(player.Id, "B", Now.UtcDateTime.AddDays(1));
        var recentlyRevoked = Session(player.Id, "C", Now.UtcDateTime.AddDays(20));
        recentlyRevoked.Revoke(Now.UtcDateTime.AddDays(-2), "logout");
        var oldRevoked = Session(player.Id, "D", Now.UtcDateTime.AddDays(20));
        oldRevoked.Revoke(Now.UtcDateTime.AddDays(-8), "rotated");
        db.RefreshTokenSessions.AddRange(oldExpired, active, recentlyRevoked, oldRevoked);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new RefreshSessionCleanupService(db, Options(batchSize: 10), new StubTimeProvider());

        var deleted = await service.ExecuteAsync();
        var remaining = await db.RefreshTokenSessions.Select(x => x.TokenHash).ToListAsync();

        Assert.Equal(2, deleted);
        Assert.Contains(active.TokenHash, remaining);
        Assert.Contains(recentlyRevoked.TokenHash, remaining);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAtConfiguredBatchLimit()
    {
        await using var db = CreateContext();
        var player = await AddPlayerAsync(db);
        for (var index = 0; index < 5; index++)
            db.RefreshTokenSessions.Add(Session(player.Id, index.ToString(), Now.UtcDateTime.AddDays(-30)));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new RefreshSessionCleanupService(db, Options(batchSize: 2, maxBatches: 1), new StubTimeProvider());

        var deleted = await service.ExecuteAsync();

        Assert.Equal(2, deleted);
        Assert.Equal(3, await db.RefreshTokenSessions.CountAsync());
    }

    private static RefreshTokenSession Session(Guid playerId, string seed, DateTime expires) =>
        RefreshTokenSession.Create(playerId, Guid.NewGuid(), seed.PadLeft(64, '0'), Now.UtcDateTime.AddDays(-60), expires);
    private static RefreshSessionCleanupOptions Options(int batchSize, int maxBatches = 10) => new()
    { RetentionDays = 7, BatchSize = batchSize, MaxBatchesPerRun = maxBatches };
    private static MergeGameDbContext CreateContext() => new(new DbContextOptionsBuilder<MergeGameDbContext>()
        .UseInMemoryDatabase($"cleanup-{Guid.NewGuid()}").Options);
    private static async Task<Player> AddPlayerAsync(MergeGameDbContext db)
    {
        var player = Player.CreateGuest(Guid.NewGuid(), new string('F', 64), Now.UtcDateTime);
        db.Players.Add(player); await db.SaveChangesAsync(); return player;
    }
    private sealed class StubTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
