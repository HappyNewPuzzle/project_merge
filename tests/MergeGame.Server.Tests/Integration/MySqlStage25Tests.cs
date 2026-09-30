using MergeGame.Server.Application.Boards;
using MergeGame.Server.Application.Game;
using MergeGame.Server.Application.Generators;
using MergeGame.Server.Application.Inventory;
using MergeGame.Server.Application.Quests;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Inventory;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Generators;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Quests;
using MergeGame.Server.Infrastructure.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace MergeGame.Server.Tests.Integration;

/// <summary>
/// SV-026: 실제 MySQL 8의 마이그레이션, 제약 조건, 트랜잭션과 동시성을 검사합니다.
/// 환경 변수가 없을 때만 건너뛰며 CI에는 전용 관리자 연결을 반드시 주입합니다.
/// 매 테스트는 고유한 임시 DB만 만들고 마지막에 그 DB만 삭제합니다.
/// </summary>
public sealed class MySqlStage25Tests
{
    private const string Stage24Migration = "20260815031948_AddAdminApprovalWorkflow";
    private const string Stage25Migration = "20260910132623_AddItemDiscoveries";
    private static readonly DateTime UtcNow = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeProvider Clock = new FixedClock();
    private static readonly InMemoryItemCatalog Items = new();
    private static readonly InMemoryGeneratorCatalog Generators = new();
    private static readonly InMemoryQuestCatalog Quests = new();

    [MySqlFact]
    public async Task EmptyDatabase_MigratesAndPreservesDiscoveriesAcrossGameplay()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await using var db = database.Open();
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(Stage25Migration, await db.Database.GetAppliedMigrationsAsync());

        var playerId = Guid.NewGuid();
        db.Players.Add(Player.CreateGuest(playerId, new string('A', 64), UtcNow));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var bootstrap = await Bootstrap(db, playerId);
        Assert.NotNull(bootstrap);
        Assert.Equal(35, bootstrap.Board.Width * bootstrap.Board.Height);
        Assert.Contains(bootstrap.Collection.DiscoveredItems, x => x.ChainId == "garden" && x.Level == 1);
        db.ChangeTracker.Clear();

        var produced = await Produce(db, playerId, "toy_basic", 1, 1, "sv026-toy");
        Assert.True(produced.Success);
        Assert.Equal("toy", produced.Response!.GeneratedItem.ChainId);
        Assert.Equal(2, produced.Response.TargetSlot);
        db.ChangeTracker.Clear();
        Assert.Equal(99, (await db.PlayerEconomies.SingleAsync()).Energy);
        Assert.Equal(4, (await db.PlayerGenerators.SingleAsync(x => x.GeneratorId == "toy_basic")).Charges);
        Assert.Contains((await ItemCollection.ReadAsync(db, playerId)).DiscoveredItems,
            x => x.ChainId == "toy" && x.Level == 1);

        var replay = await Produce(db, playerId, "toy_basic", 1, 1, "sv026-toy");
        Assert.True(replay.Response!.Replayed);
        Assert.Equal(produced.Response.GeneratedItem.ItemId, replay.Response.GeneratedItem.ItemId);
        var staleBoard = await Produce(db, playerId, "food_basic", 1, 2, "sv026-stale-board");
        var staleEconomy = await Produce(db, playerId, "food_basic", 2, 1, "sv026-stale-economy");
        Assert.Equal(GeneratorProduceError.StaleRevision, staleBoard.Error);
        Assert.Equal(GeneratorProduceError.StaleRevision, staleEconomy.Error);
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.BoardItems.CountAsync());
        Assert.Equal(99, (await db.PlayerEconomies.SingleAsync()).Energy);
        Assert.False(await db.PlayerGenerators.AnyAsync(x => x.GeneratorId == "food_basic" && x.Charges < 5));

        var merged = await new MergeBoardItemsService(db, Items, Clock, Progress(db))
            .ExecuteAsync(playerId, 0, 1, 2);
        Assert.Equal(BoardMergeServiceStatus.Succeeded, merged.Status);
        db.ChangeTracker.Clear();
        Assert.Contains((await ItemCollection.ReadAsync(db, playerId)).DiscoveredItems,
            x => x.ChainId == "garden" && x.Level == 2);

        var sold = await new SellBoardItemService(db, Items, Clock, Progress(db))
            .ExecuteAsync(playerId, produced.Response.GeneratedItem.ItemId, 3, 2, "sv026-sell");
        Assert.True(sold.Success);
        db.ChangeTracker.Clear();
        Assert.False(await db.BoardItems.AnyAsync(x => x.Id == produced.Response.GeneratedItem.ItemId));
        Assert.Contains((await ItemCollection.ReadAsync(db, playerId)).DiscoveredItems,
            x => x.ChainId == "toy" && x.Level == 1);

        var gardenItemId = (await db.BoardItems.SingleAsync()).Id;
        var transfer = new TransferInventoryItemService(db, Items, Clock);
        var stored = await transfer.StoreAsync(playerId, gardenItemId, 4, 1, "sv026-store");
        Assert.True(stored.Success);
        db.ChangeTracker.Clear();
        Assert.Contains((await ItemCollection.ReadAsync(db, playerId)).DiscoveredItems,
            x => x.ChainId == "garden" && x.Level == 2);
        var restored = await transfer.RestoreAsync(playerId, gardenItemId, 5, 2, "sv026-restore");
        Assert.True(restored.Success);
        db.ChangeTracker.Clear();
        Assert.Equal(3, (await ItemCollection.ReadAsync(db, playerId)).DiscoveredItems.Count);
    }

    [MySqlFact]
    public async Task Stage24Database_BackfillsOnlyCurrentlyOwnedBoardAndInventoryItems()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await using var db = database.Open();
        await db.GetService<IMigrator>().MigrateAsync(Stage24Migration);
        Assert.DoesNotContain(Stage25Migration, await db.Database.GetAppliedMigrationsAsync());
        var playerId = Guid.NewGuid();
        db.Players.Add(Player.CreateGuest(playerId, new string('B', 64), UtcNow));
        db.PlayerBoards.Add(PlayerBoard.CreateInitial(playerId, UtcNow));
        db.PlayerInventories.Add(PlayerInventory.CreateInitial(playerId, UtcNow));
        await db.SaveChangesAsync();
        // 보드에도 있는 garden:1을 중복 보관하고 food:1을 한 개 보관합니다.
        // 이미 판매된 toy:1은 어디에도 없으므로 추측해서 복원해서는 안 됩니다.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO inventory_items (id, player_id, chain_id, level) VALUES ({Guid.NewGuid()}, {playerId}, {"garden"}, {1}), ({Guid.NewGuid()}, {playerId}, {"food"}, {1})");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var discovered = (await ItemCollection.ReadAsync(db, playerId)).DiscoveredItems;
        Assert.Equal(2, discovered.Count);
        Assert.Contains(discovered, x => x.ChainId == "garden" && x.Level == 1);
        Assert.Contains(discovered, x => x.ChainId == "food" && x.Level == 1);
        Assert.DoesNotContain(discovered, x => x.ChainId == "toy");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [MySqlFact]
    public async Task ConcurrentDuplicateDiscovery_UsesDatabaseUniqueKey()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await using (var setup = database.Open())
        {
            await setup.Database.MigrateAsync();
            setup.Players.Add(Player.CreateGuest(database.PlayerId, new string('C', 64), UtcNow));
            await setup.SaveChangesAsync();
        }

        async Task<bool> InsertAsync()
        {
            await using var db = database.Open();
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO item_discoveries (player_id, chain_id, level) VALUES ({database.PlayerId}, {"toy"}, {1})");
                return true;
            }
            catch (MySqlException error) when (error.Number == 1062) { return false; }
        }

        var results = await Task.WhenAll(InsertAsync(), InsertAsync());
        Assert.Equal(1, results.Count(x => x));
        await using var check = database.Open();
        Assert.Single(await check.ItemDiscoveries.ToListAsync());
    }

    [MySqlFact]
    public async Task ConcurrentSameIdempotencyKey_ChangesBoardEconomyAndChargeOnce()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await using (var setup = database.Open())
        {
            await setup.Database.MigrateAsync();
            setup.Players.Add(Player.CreateGuest(database.PlayerId, new string('D', 64), UtcNow));
            setup.PlayerBoards.Add(PlayerBoard.CreateInitial(database.PlayerId, UtcNow));
            setup.PlayerEconomies.Add(PlayerEconomy.CreateInitial(database.PlayerId, UtcNow));
            await setup.SaveChangesAsync();
        }

        async Task<GeneratorProduceResult> RunAsync()
        {
            await using var db = database.Open();
            return await Produce(db, database.PlayerId, "toy_basic", 1, 1, "sv026-concurrent-key");
        }

        var results = await Task.WhenAll(RunAsync(), RunAsync());
        Assert.Contains(results, x => x.Success && !x.Response!.Replayed);
        Assert.All(results, x => Assert.True(x.Success || x.Error == GeneratorProduceError.StaleRevision));
        await using var check = database.Open();
        Assert.Equal(3, await check.BoardItems.CountAsync());
        Assert.Equal(99, (await check.PlayerEconomies.SingleAsync()).Energy);
        Assert.Equal(4, (await check.PlayerGenerators.SingleAsync()).Charges);
        Assert.Single(await check.GeneratorProductionReceipts.ToListAsync());
        // 생성 서비스가 추적 중인 기존 garden:1과 신규 toy:1을 모두 기록합니다.
        Assert.Equal(2, await check.ItemDiscoveries.CountAsync());
        var replay = await Produce(check, database.PlayerId, "toy_basic", 1, 1, "sv026-concurrent-key");
        Assert.True(replay.Response!.Replayed);
    }

    [MySqlFact]
    public async Task FailedDiscoveryInsert_RollsBackGeneratedItemLedgerReceiptAndEnergy()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await using var db = database.Open();
        await db.Database.MigrateAsync();
        db.Players.Add(Player.CreateGuest(database.PlayerId, new string('E', 64), UtcNow));
        db.PlayerBoards.Add(PlayerBoard.CreateInitial(database.PlayerId, UtcNow));
        db.PlayerEconomies.Add(PlayerEconomy.CreateInitial(database.PlayerId, UtcNow));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // SaveChanges 중 도감 INSERT만 실패시켜 앞선 보드·경제·영수증 변경도 함께 취소되는지 봅니다.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER sv026_reject_discovery BEFORE INSERT ON item_discoveries
            FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'sv026 forced rollback'
            """);
        var result = await Produce(db, database.PlayerId, "toy_basic", 1, 1, "sv026-rollback");
        Assert.Equal(GeneratorProduceError.StaleRevision, result.Error);
        await using var check = database.Open();
        Assert.Equal(2, await check.BoardItems.CountAsync());
        Assert.Equal(1, (await check.PlayerBoards.SingleAsync()).Revision);
        Assert.Equal(100, (await check.PlayerEconomies.SingleAsync()).Energy);
        Assert.Empty(await check.PlayerGenerators.ToListAsync());
        Assert.Empty(await check.GeneratorProductionReceipts.ToListAsync());
        Assert.Empty(await check.EconomyLedgerEntries.ToListAsync());
        Assert.Empty(await check.ItemDiscoveries.ToListAsync());
    }

    private static QuestProgressService Progress(MergeGameDbContext db) => new(db, Quests);

    private static Task<GeneratorProduceResult> Produce(
        MergeGameDbContext db, Guid playerId, string generatorId, long boardRevision,
        long economyRevision, string key) => new ProduceGeneratorItemService(
            db, Items, Generators, Clock, Progress(db))
        .ExecuteAsync(playerId, generatorId, boardRevision, economyRevision, key);

    private static Task<GameBootstrapResponse?> Bootstrap(MergeGameDbContext db, Guid playerId) =>
        new GameBootstrapService(db, Items, Generators, new FixedFriendCode(), Quests, Clock)
            .ExecuteAsync(playerId);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class FixedFriendCode : IFriendCodeGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _adminConnection;
        private readonly string _databaseName;
        private readonly DbContextOptions<MergeGameDbContext> _options;
        public Guid PlayerId { get; } = Guid.NewGuid();

        private TemporaryDatabase(string adminConnection, string name)
        {
            _adminConnection = adminConnection;
            _databaseName = name;
            // DROP DATABASE 직후 다음 테스트가 종료된 세션을 풀에서 재사용하지 않도록 격리합니다.
            var builder = new MySqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = false };
            _options = new DbContextOptionsBuilder<MergeGameDbContext>()
                .UseMySql(builder.ConnectionString, new MySqlServerVersion(new Version(8, 0, 36)))
                .Options;
        }

        public MergeGameDbContext Open() => new(_options);

        public static async Task<TemporaryDatabase> CreateAsync()
        {
            var connection = Environment.GetEnvironmentVariable("SV026_MYSQL_ADMIN");
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("SV026_MYSQL_ADMIN is required for MySQL integration tests.");
            var name = $"mergegame_sv026_{Guid.NewGuid():N}";
            var database = new TemporaryDatabase(connection, name);
            await database.ExecuteAdminAsync($"CREATE DATABASE `{name}` CHARACTER SET utf8mb4");
            return database;
        }

        public async ValueTask DisposeAsync() =>
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `{_databaseName}`");

        private async Task ExecuteAdminAsync(string sql)
        {
            var builder = new MySqlConnectionStringBuilder(_adminConnection) { Database = "mysql", Pooling = false };
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>개발자 PC에 MySQL이 없으면 결과에 명시적인 skip을 남깁니다.</summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SV026_MYSQL_ADMIN")))
            Skip = "SV026_MYSQL_ADMIN is not configured";
    }
}
