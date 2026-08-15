using MergeGame.Server.Application.Quests;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Quests;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Quests;

/// <summary>다중 퀘스트 생성, 이벤트별 진행과 UTC 기간 초기화를 검증합니다.</summary>
public sealed class QuestProgressServiceTests
{
    [Fact]
    public async Task InitializeAndRecord_CreatesFiveQuestsAndUpdatesMatchingDailyQuest()
    {
        var now = new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), now.GetUtcNow().UtcDateTime);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        var catalog = new InMemoryQuestCatalog();
        var query = new QuestQueryService(db, catalog, now);

        var initialized = await query.InitializeAsync(player.Id);
        await new QuestProgressService(db, catalog).RecordAsync(
            player.Id, "item_generated", now.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();

        Assert.Equal(5, initialized!.Count);
        var generated = await db.PlayerQuests.SingleAsync(value => value.QuestId == "daily_generate_5");
        Assert.Equal(1, generated.CurrentCount);
        Assert.All(await db.PlayerQuests.Where(value => value.QuestId != "daily_generate_5").ToListAsync(),
            value => Assert.Equal(0, value.CurrentCount));
    }

    [Fact]
    public async Task GetAsync_OnNextUtcDay_ResetsDailyButKeepsLifetimeProgress()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 15, 1, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), time.GetUtcNow().UtcDateTime);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        var catalog = new InMemoryQuestCatalog();
        var query = new QuestQueryService(db, catalog, time);
        await query.InitializeAsync(player.Id);
        var progress = new QuestProgressService(db, catalog);
        await progress.RecordAsync(player.Id, "item_generated", time.GetUtcNow().UtcDateTime);
        await progress.RecordAsync(player.Id, "item_merged", time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();

        time.Now = time.Now.AddDays(1);
        db.ChangeTracker.Clear();
        var nextDay = Assert.IsAssignableFrom<IReadOnlyList<QuestSnapshot>>(
            await query.GetAsync(player.Id));

        Assert.Equal(0, nextDay.Single(value => value.QuestId == "daily_generate_5").CurrentCount);
        Assert.Equal(1, nextDay.Single(value => value.QuestId == "merge_3").CurrentCount);
    }

    private static MergeGameDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"quest-progress-{Guid.NewGuid()}").Options);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
