using MergeGame.Server.Application.Administration;
using MergeGame.Server.Domain.Authentication;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Administration;

/// <summary>정지 상태·세션 폐기·감사 원장의 원자성과 멱등 재시도를 검증합니다.</summary>
public sealed class ChangePlayerSuspensionServiceTests
{
    [Fact]
    public async Task SuspendThenReplay_ChangesOnceAndRevokesActiveSession()
    {
        var now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        await using var db = new MergeGameDbContext(new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"moderation-{Guid.NewGuid()}").Options);
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), now.UtcDateTime);
        db.Players.Add(player);
        db.RefreshTokenSessions.Add(RefreshTokenSession.Create(player.Id, Guid.NewGuid(), new string('B', 64),
            now.UtcDateTime, now.UtcDateTime.AddDays(30)));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new ChangePlayerSuspensionService(db, new StubTimeProvider(now));

        var changed = await service.ExecuteAsync(player.Id, "operator-a", "suspend-request-001", true,
            "비정상 재화 사용 조사", 0);
        db.ChangeTracker.Clear();
        var replayed = await service.ExecuteAsync(player.Id, "operator-a", "suspend-request-001", true,
            "비정상 재화 사용 조사", 0);

        Assert.Equal(SuspensionChangeStatus.Succeeded, changed.Status);
        Assert.Equal(SuspensionChangeStatus.Replayed, replayed.Status);
        Assert.True((await db.PlayerModerations.SingleAsync()).IsSuspended);
        Assert.NotNull((await db.RefreshTokenSessions.SingleAsync()).RevokedAtUtc);
        Assert.Single(await db.AdminActionAudits.ToListAsync());
    }

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
