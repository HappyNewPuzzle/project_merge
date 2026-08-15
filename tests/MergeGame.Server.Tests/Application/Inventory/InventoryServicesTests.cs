using MergeGame.Server.Application.Inventory;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Inventory;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Inventory;

/// <summary>보드와 인벤토리 왕복 이동이 ID를 유지하고 멱등하게 저장되는지 검증합니다.</summary>
public sealed class InventoryServicesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StoreThenRestore_PreservesItemIdAndServerSelectsEmptySlot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var itemId = (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync())
            .Items.Single(value => value.SlotIndex == 0).Id;
        fixture.Db.ChangeTracker.Clear();

        var stored = await fixture.Service.StoreAsync(fixture.PlayerId, itemId, 1, 1, "store-1");
        fixture.Db.ChangeTracker.Clear();
        var restored = await fixture.Service.RestoreAsync(fixture.PlayerId, itemId, 2, 2, "restore-1");

        Assert.Equal("store", stored.Response!.Action);
        Assert.Equal("restore", restored.Response!.Action);
        Assert.Equal(0, restored.Response.TargetSlot);
        Assert.Equal(itemId, restored.Response.ItemId);
        Assert.Empty(restored.Response.Inventory.Items);
        Assert.Contains(restored.Response.Board.Items, value => value.ItemId == itemId && value.SlotIndex == 0);
    }

    [Fact]
    public async Task Store_SameKey_ReplaysWithoutMovingAnotherItem()
    {
        await using var fixture = await Fixture.CreateAsync();
        var itemId = (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync()).Items.First().Id;
        fixture.Db.ChangeTracker.Clear();
        await fixture.Service.StoreAsync(fixture.PlayerId, itemId, 1, 1, "same-store");
        fixture.Db.ChangeTracker.Clear();

        var replay = await fixture.Service.StoreAsync(fixture.PlayerId, itemId, 1, 1, "same-store");

        Assert.True(replay.Response!.Replayed);
        Assert.Single((await fixture.Db.PlayerInventories.Include(value => value.Items).SingleAsync()).Items);
        Assert.Single(await fixture.Db.InventoryTransferReceipts.ToListAsync());
    }

    [Fact]
    public async Task Store_StaleInventoryRevision_DoesNotRemoveBoardItem()
    {
        await using var fixture = await Fixture.CreateAsync();
        var itemId = (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync()).Items.First().Id;
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.StoreAsync(fixture.PlayerId, itemId, 1, 0, "stale-store");

        Assert.Equal(InventoryServiceError.StaleRevision, result.Error);
        Assert.Equal(2, (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync()).Items.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(MergeGameDbContext db, Guid playerId)
        {
            Db = db;
            PlayerId = playerId;
            Service = new TransferInventoryItemService(db, new InMemoryItemCatalog(), new StubTimeProvider());
        }
        public MergeGameDbContext Db { get; }
        public Guid PlayerId { get; }
        public TransferInventoryItemService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new MergeGameDbContext(new DbContextOptionsBuilder<MergeGameDbContext>()
                .UseInMemoryDatabase($"inventory-{Guid.NewGuid()}").Options);
            var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), Now.UtcDateTime);
            db.Players.Add(player);
            db.PlayerBoards.Add(PlayerBoard.CreateInitial(player.Id, Now.UtcDateTime));
            db.PlayerInventories.Add(PlayerInventory.CreateInitial(player.Id, Now.UtcDateTime));
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
