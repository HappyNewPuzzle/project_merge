using MergeGame.Server.Application.Generators;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Generators;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Generators;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MergeGame.Server.Application.Quests;
using MergeGame.Server.Infrastructure.Quests;

namespace MergeGame.Server.Tests.Application.Generators;

/// <summary>
/// 서버 권위형 생성기가 슬롯·아이템을 결정하고, 실패와 재시도에서는 재화를 중복 소비하지 않는지 검증합니다.
/// </summary>
public sealed class ProduceGeneratorItemServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ChoosesSlotAndPersistsAllStates()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "garden", 1, 1, "produce-001");

        fixture.Db.ChangeTracker.Clear();
        var board = await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync();
        var economy = await fixture.Db.PlayerEconomies.SingleAsync();
        var generator = await fixture.Db.PlayerGenerators.SingleAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.Response!.TargetSlot);
        Assert.Equal("garden", result.Response.GeneratedItem.ChainId);
        Assert.Equal(1, result.Response.GeneratedItem.Level);
        Assert.Equal(2, board.Revision);
        Assert.Equal(3, board.Items.Count);
        Assert.Equal(99, economy.Energy);
        Assert.Equal(2, economy.Revision);
        Assert.Equal(4, generator.Charges);
        Assert.Single(await fixture.Db.GeneratorProductionReceipts.ToListAsync());
        var ledger = Assert.Single(await fixture.Db.EconomyLedgerEntries.ToListAsync());
        Assert.Equal("generator.energy_spent", ledger.Reason);
        Assert.Equal(-1, ledger.Delta);
        Assert.Equal(1, (await fixture.Db.PlayerQuests.SingleAsync(
            value => value.QuestId == "daily_generate_5")).CurrentCount);
    }

    [Theory]
    [InlineData("toy_basic", "toy")]
    [InlineData("food_basic", "food")]
    [InlineData("rest_basic", "rest")]
    public async Task ExecuteAsync_ProductionGenerator_CreatesServerDefinedLevelOneItem(
        string generatorId,
        string expectedChainId)
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, generatorId, 1, 1, $"produce-{generatorId}");

        Assert.True(result.Success);
        Assert.Equal(2, result.Response!.TargetSlot);
        Assert.Equal(expectedChainId, result.Response.GeneratedItem.ChainId);
        Assert.Equal(1, result.Response.GeneratedItem.Level);
        Assert.Equal(2, result.Response.Board.Revision);
        Assert.Equal(99, result.Response.Economy.Energy);
        Assert.Equal(2, result.Response.Economy.Revision);
        Assert.Equal(generatorId, result.Response.Generator.GeneratorId);
        Assert.Equal(4, result.Response.Generator.Charges);
        Assert.Equal(2, result.Response.Generator.Revision);
    }

    [Fact]
    public async Task ExecuteAsync_ConsumesOnlySelectedGeneratorCharge()
    {
        await using var fixture = await Fixture.CreateAsync();
        var catalog = new InMemoryGeneratorCatalog();
        Assert.True(catalog.TryGet("food_basic", out var untouchedDefinition));
        fixture.Db.PlayerGenerators.Add(PlayerGenerator.CreateInitial(
            fixture.PlayerId, untouchedDefinition, Now.UtcDateTime));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "toy_basic", 1, 1, "independent-charge-key");

        fixture.Db.ChangeTracker.Clear();
        var states = await fixture.Db.PlayerGenerators.ToDictionaryAsync(value => value.GeneratorId);
        Assert.True(result.Success);
        Assert.Equal(4, states["toy_basic"].Charges);
        Assert.Equal(2, states["toy_basic"].Revision);
        Assert.Equal(5, states["food_basic"].Charges);
        Assert.Equal(1, states["food_basic"].Revision);
    }

    [Fact]
    public async Task ExecuteAsync_SameIdempotencyKey_ReplaysWithoutSecondChargeOrItem()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "garden", 1, 1, "retry-key");

        // 최초 성공 뒤 revision은 이미 바뀌었지만, 영수증 조회가 revision 검사보다 앞서야 합니다.
        fixture.Db.ChangeTracker.Clear();
        var replay = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "garden", 1, 1, "retry-key");

        fixture.Db.ChangeTracker.Clear();
        var board = await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync();
        var economy = await fixture.Db.PlayerEconomies.SingleAsync();
        var generator = await fixture.Db.PlayerGenerators.SingleAsync();

        Assert.True(first.Success);
        Assert.True(replay.Success);
        Assert.True(replay.Response!.Replayed);
        Assert.Equal(first.Response!.GeneratedItem.ItemId, replay.Response.GeneratedItem.ItemId);
        Assert.Equal(3, board.Items.Count);
        Assert.Equal(99, economy.Energy);
        Assert.Equal(4, generator.Charges);
        Assert.Single(await fixture.Db.GeneratorProductionReceipts.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SameIdempotencyKeyForDifferentGenerator_ReturnsConflictWithoutSecondMutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "toy_basic", 1, 1, "shared-generator-key");
        fixture.Db.ChangeTracker.Clear();

        var conflict = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "food_basic", 2, 2, "shared-generator-key");

        fixture.Db.ChangeTracker.Clear();
        var board = await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync();
        var economy = await fixture.Db.PlayerEconomies.SingleAsync();
        var generators = await fixture.Db.PlayerGenerators.ToListAsync();
        Assert.True(first.Success);
        Assert.Equal(GeneratorProduceError.IdempotencyKeyConflict, conflict.Error);
        Assert.Equal(3, board.Items.Count);
        Assert.Equal(99, economy.Energy);
        var generator = Assert.Single(generators);
        Assert.Equal("toy_basic", generator.GeneratorId);
        Assert.Equal(4, generator.Charges);
        Assert.Single(await fixture.Db.GeneratorProductionReceipts.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownGenerator_ReturnsDedicatedError()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "unknown", 1, 1, "unknown-key");

        Assert.False(result.Success);
        Assert.Equal(GeneratorProduceError.UnknownGenerator, result.Error);
        Assert.Empty(fixture.Db.GeneratorProductionReceipts);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task ExecuteAsync_StaleEitherRevision_DoesNotMutateAnyState(
        long expectedBoardRevision,
        long expectedEconomyRevision)
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "toy_basic", expectedBoardRevision, expectedEconomyRevision,
            $"stale-{expectedBoardRevision}-{expectedEconomyRevision}");

        Assert.Equal(GeneratorProduceError.StaleRevision, result.Error);
        Assert.Equal(1, result.Board!.Revision);
        Assert.Equal(100, result.Economy!.Energy);
        Assert.Equal(2, (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync()).Items.Count);
        Assert.Equal(1, (await fixture.Db.PlayerEconomies.SingleAsync()).Revision);
        Assert.Empty(await fixture.Db.PlayerGenerators.ToListAsync());
        Assert.Empty(await fixture.Db.GeneratorProductionReceipts.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_NoEmptySlot_ReturnsFullBoardWithoutSpendingEnergy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var board = await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync();
        var catalog = new InMemoryItemCatalog();
        for (var slot = 2; slot < PlayerBoard.SlotCount; slot++)
        {
            var add = board.TryAddGeneratedItem(slot, board.Revision, "garden", 1, catalog, Now.UtcDateTime);
            fixture.Db.BoardItems.Add(add.GeneratedItem!);
        }
        await fixture.Db.SaveChangesAsync();
        var fullRevision = board.Revision;
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "garden", fullRevision, 1, "full-key");

        Assert.Equal(GeneratorProduceError.FullBoard, result.Error);
        Assert.Equal(100, (await fixture.Db.PlayerEconomies.SingleAsync()).Energy);
        Assert.Empty(await fixture.Db.PlayerGenerators.ToListAsync());
        Assert.Empty(await fixture.Db.GeneratorProductionReceipts.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyEnergy_ReturnsInsufficientEnergyWithoutCreatingItem()
    {
        await using var fixture = await Fixture.CreateAsync();
        var economy = await fixture.Db.PlayerEconomies.SingleAsync();
        for (var index = 0; index < PlayerEconomy.MaxEnergy; index++)
        {
            Assert.Equal(EconomyActionError.None, economy.TrySpendGeneratorEnergy(economy.Revision, Now.UtcDateTime));
        }
        await fixture.Db.SaveChangesAsync();
        var economyRevision = economy.Revision;
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "garden", 1, economyRevision, "energy-key");

        Assert.Equal(GeneratorProduceError.InsufficientEnergy, result.Error);
        Assert.Equal(2, (await fixture.Db.PlayerBoards.Include(value => value.Items).SingleAsync()).Items.Count);
        Assert.Empty(fixture.Db.PlayerGenerators);
    }

    [Fact]
    public async Task ExecuteAsync_NoGeneratorCharge_ReturnsCooldownState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var catalog = new InMemoryGeneratorCatalog();
        Assert.True(catalog.TryGet("garden", out var definition));
        var generator = PlayerGenerator.CreateInitial(fixture.PlayerId, definition, Now.UtcDateTime);
        for (var index = 0; index < definition.MaxCharges; index++)
        {
            Assert.True(generator.TryConsumeCharge(Now.UtcDateTime, definition));
        }
        fixture.Db.PlayerGenerators.Add(generator);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.ExecuteAsync(
            fixture.PlayerId, "garden", 1, 1, "cooldown-key");

        Assert.Equal(GeneratorProduceError.GeneratorCooldown, result.Error);
        Assert.True(result.Generator!.IsCoolingDown);
        Assert.Equal(30, result.Generator.CooldownRemainingSeconds);
        Assert.Equal(100, (await fixture.Db.PlayerEconomies.SingleAsync()).Energy);
        var persisted = await fixture.Db.PlayerGenerators.SingleAsync();
        Assert.Equal(0, persisted.Charges);
        Assert.Equal(6, persisted.Revision);
        Assert.Empty(await fixture.Db.GeneratorProductionReceipts.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(MergeGameDbContext db, Guid playerId)
        {
            Db = db;
            PlayerId = playerId;
            Service = new ProduceGeneratorItemService(
                db,
                new InMemoryItemCatalog(),
                new InMemoryGeneratorCatalog(),
                new StubTimeProvider(Now),
                new QuestProgressService(db, new InMemoryQuestCatalog()));
        }

        public MergeGameDbContext Db { get; }
        public Guid PlayerId { get; }
        public ProduceGeneratorItemService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<MergeGameDbContext>()
                .UseInMemoryDatabase($"authoritative-generator-{Guid.NewGuid()}")
                .Options;
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

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
