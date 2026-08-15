using MergeGame.Server.Application.Economy;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Economy;

/// <summary>
/// 생성기 사용이 경제와 보드를 하나의 저장 작업으로 갱신하는지 검증합니다.
/// </summary>
public sealed class GenerateBoardItemServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WithEmptySlot_SpendsEnergyAndAddsItem()
    {
        var options = new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"generator-{Guid.NewGuid()}")
            .Options;
        await using var dbContext = new MergeGameDbContext(options);
        var player = Player.CreateGuest(
            Guid.NewGuid(),
            new string('A', 64),
            FixedNow.UtcDateTime);
        dbContext.Players.Add(player);
        dbContext.PlayerBoards.Add(
            PlayerBoard.CreateInitial(player.Id, FixedNow.UtcDateTime));
        dbContext.PlayerEconomies.Add(
            PlayerEconomy.CreateInitial(player.Id, FixedNow.UtcDateTime));
        await dbContext.SaveChangesAsync();
        // 실제 HTTP 요청은 이전 초기화 요청과 다른 scoped DbContext를 사용하므로 추적 상태를 비워 재현합니다.
        dbContext.ChangeTracker.Clear();

        var service = new GenerateBoardItemService(
            dbContext,
            new InMemoryItemCatalog(),
            new StubTimeProvider(FixedNow));
        var result = await service.ExecuteAsync(
            player.Id,
            targetSlot: 2,
            expectedBoardRevision: 1,
            expectedEconomyRevision: 1);

        dbContext.ChangeTracker.Clear();
        var board = await dbContext.PlayerBoards.Include(value => value.Items).SingleAsync();
        var economy = await dbContext.PlayerEconomies.SingleAsync();

        Assert.Equal(EconomyServiceStatus.Succeeded, result.Status);
        Assert.Equal(2, board.Revision);
        Assert.Equal(3, board.Items.Count);
        Assert.Contains(board.Items, item => item.SlotIndex == 2 && item.Level == 1);
        Assert.Equal(99, economy.Energy);
        Assert.Equal(2, economy.Revision);
        Assert.Contains(await dbContext.EconomyLedgerEntries.ToListAsync(),
            value => value.Reason == "generator.energy_spent" && value.Delta == -1);
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public StubTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
