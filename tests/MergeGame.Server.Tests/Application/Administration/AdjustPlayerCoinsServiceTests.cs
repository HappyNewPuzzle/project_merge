using MergeGame.Server.Application.Administration;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Administration;

/// <summary>코인 조정의 멱등 지급, 음수 잔액 방지와 감사 원장 저장을 검증합니다.</summary>
public sealed class AdjustPlayerCoinsServiceTests
{
    [Fact]
    public async Task CreditThenReplay_AppliesOnceAndStoresAudit()
    {
        await using var db = CreateContext(); var player = await SeedAsync(db);
        var service = CreateService(db);

        var changed = await service.ExecuteAsync(player.Id, "operator-a", "coin-ticket-001", 500,
            "고객 지원 보상", 1);
        db.ChangeTracker.Clear();
        var replayed = await service.ExecuteAsync(player.Id, "operator-a", "coin-ticket-001", 500,
            "고객 지원 보상", 1);

        Assert.Equal(CoinAdjustmentStatus.Succeeded, changed.Status);
        Assert.Equal(CoinAdjustmentStatus.Replayed, replayed.Status);
        Assert.Equal(500, (await db.PlayerEconomies.SingleAsync()).Coins);
        Assert.Single(await db.AdminActionAudits.ToListAsync());
    }

    [Fact]
    public async Task DebitBeyondBalance_DoesNotChangeEconomyOrWriteAudit()
    {
        await using var db = CreateContext(); var player = await SeedAsync(db);
        var result = await CreateService(db).ExecuteAsync(player.Id, "operator-a", "coin-ticket-002", -1,
            "잘못 지급된 코인 회수", 1);
        Assert.Equal(CoinAdjustmentStatus.InvalidBalance, result.Status);
        Assert.Equal(0, (await db.PlayerEconomies.SingleAsync()).Coins);
        Assert.Empty(await db.AdminActionAudits.ToListAsync());
    }

    [Fact]
    public async Task AmountBeyondConfiguredLimit_IsRejectedBeforeMutation()
    {
        await using var db = CreateContext(); var player = await SeedAsync(db);
        var result = await CreateService(db).ExecuteAsync(player.Id, "operator-a", "coin-ticket-003", 10_001,
            "한도 초과 요청", 1);
        Assert.Equal(CoinAdjustmentStatus.InvalidAmount, result.Status);
        Assert.Equal(0, (await db.PlayerEconomies.SingleAsync()).Coins);
        Assert.Empty(await db.AdminActionAudits.ToListAsync());
    }

    private static AdjustPlayerCoinsService CreateService(MergeGameDbContext db) =>
        new(db, TimeProvider.System, new AdminApiOptions { MaxAbsoluteCoinAdjustment = 10_000 });
    private static MergeGameDbContext CreateContext() => new(new DbContextOptionsBuilder<MergeGameDbContext>()
        .UseInMemoryDatabase($"coin-admin-{Guid.NewGuid()}").Options);
    private static async Task<Player> SeedAsync(MergeGameDbContext db)
    {
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), DateTime.UtcNow);
        db.Players.Add(player); db.PlayerEconomies.Add(PlayerEconomy.CreateInitial(player.Id, DateTime.UtcNow));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return player;
    }
}
