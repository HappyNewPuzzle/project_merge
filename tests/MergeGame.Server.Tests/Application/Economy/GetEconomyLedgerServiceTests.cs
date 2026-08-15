using MergeGame.Server.Application.Economy;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Economy;

/// <summary>경제 원장이 인증 플레이어 범위와 최신순 제한을 지키는지 검증합니다.</summary>
public sealed class GetEconomyLedgerServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsOnlyPlayerEntriesNewestFirstAndClampsLimit()
    {
        var options = new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"economy-ledger-{Guid.NewGuid()}").Options;
        await using var db = new MergeGameDbContext(options);
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), DateTime.UtcNow);
        var other = Player.CreateGuest(Guid.NewGuid(), new string('B', 64), DateTime.UtcNow);
        db.Players.AddRange(player, other);
        db.EconomyLedgerEntries.AddRange(
            EconomyLedgerEntry.CreateCoins(player.Id, "test.old", 5, 5, 2, "old", DateTime.UtcNow.AddMinutes(-1)),
            EconomyLedgerEntry.CreateCoins(player.Id, "test.new", 10, 15, 3, "new", DateTime.UtcNow),
            EconomyLedgerEntry.CreateCoins(other.Id, "test.other", 7, 7, 2, "other", DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await new GetEconomyLedgerService(db).ExecuteAsync(player.Id, limit: 1);

        var entry = Assert.Single(result);
        Assert.Equal("test.new", entry.Reason);
        Assert.Equal(10, entry.Delta);
    }
}
