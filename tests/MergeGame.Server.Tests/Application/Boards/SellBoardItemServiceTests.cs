using MergeGame.Server.Application.Boards;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MergeGame.Server.Application.Quests;
using MergeGame.Server.Infrastructure.Quests;

namespace MergeGame.Server.Tests.Application.Boards;

/// <summary>아이템 제거와 코인 지급이 원자적이고 멱등하게 저장되는지 검증합니다.</summary>
public sealed class SellBoardItemServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ValidItem_RemovesItemAndCreditsCatalogPrice()
    {
        await using var fixture = await Fixture.CreateAsync();
        var itemId = (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync())
            .Items.First().Id;
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, itemId, 1, 1, "sell-001");

        fixture.Db.ChangeTracker.Clear();
        var board = await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync();
        var economy = await fixture.Db.PlayerEconomies.SingleAsync();
        Assert.True(result.Success);
        Assert.Equal(5, result.Response!.SalePrice);
        Assert.Equal(itemId, result.Response.SoldItem.ItemId);
        Assert.Single(board.Items);
        Assert.Equal(2, board.Revision);
        Assert.Equal(5, economy.Coins);
        Assert.Equal(2, economy.Revision);
        var ledger = Assert.Single(await fixture.Db.EconomyLedgerEntries.ToListAsync());
        Assert.Equal("board_item.sold", ledger.Reason);
        Assert.Equal(5, ledger.Delta);
        Assert.Equal(1, (await fixture.Db.PlayerQuests.SingleAsync(
            value => value.QuestId == "daily_sell_3")).CurrentCount);
    }

    [Fact]
    public async Task ExecuteAsync_SameKey_ReplaysWithoutSecondCoinCredit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var itemId = (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync())
            .Items.First().Id;
        fixture.Db.ChangeTracker.Clear();
        await fixture.Service.ExecuteAsync(fixture.PlayerId, itemId, 1, 1, "retry-sell");
        fixture.Db.ChangeTracker.Clear();

        var replay = await fixture.Service.ExecuteAsync(fixture.PlayerId, itemId, 1, 1, "retry-sell");

        Assert.True(replay.Response!.Replayed);
        Assert.Equal(5, (await fixture.Db.PlayerEconomies.SingleAsync()).Coins);
        Assert.Single(await fixture.Db.BoardItemSaleReceipts.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownItem_DoesNotChangeEconomy()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, Guid.NewGuid(), 1, 1, "missing-sell");

        Assert.Equal(BoardItemSaleServiceError.ItemNotFound, result.Error);
        Assert.Equal(0, (await fixture.Db.PlayerEconomies.SingleAsync()).Coins);
    }

    [Fact]
    public async Task ExecuteAsync_StaleEconomyRevision_DoesNotRemoveItem()
    {
        await using var fixture = await Fixture.CreateAsync();
        var board = await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync();
        var itemId = board.Items.First().Id;
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, itemId, 1, expectedEconomyRevision: 0, "stale-sell");

        Assert.Equal(BoardItemSaleServiceError.StaleRevision, result.Error);
        Assert.Equal(2, (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync()).Items.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(MergeGameDbContext db, Guid playerId)
        {
            Db = db;
            PlayerId = playerId;
            Service = new SellBoardItemService(
                db,
                new InMemoryItemCatalog(),
                new StubTimeProvider(),
                new QuestProgressService(db, new InMemoryQuestCatalog()));
        }
        public MergeGameDbContext Db { get; }
        public Guid PlayerId { get; }
        public SellBoardItemService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<MergeGameDbContext>()
                .UseInMemoryDatabase($"item-sale-{Guid.NewGuid()}").Options;
            var db = new MergeGameDbContext(options);
            var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), Now.UtcDateTime);
            db.Players.Add(player);
            db.PlayerBoards.Add(PlayerBoard.CreateInitial(player.Id, Now.UtcDateTime));
            db.PlayerEconomies.Add(PlayerEconomy.CreateInitial(player.Id, Now.UtcDateTime));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(db, player.Id);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
