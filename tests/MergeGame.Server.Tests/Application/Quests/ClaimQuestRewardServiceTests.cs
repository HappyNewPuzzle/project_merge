using MergeGame.Server.Application.Quests;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Quests;

/// <summary>멱등성 키 재시도가 코인을 중복 지급하지 않는지 검증합니다.</summary>
public sealed class ClaimQuestRewardServiceTests
{
    [Fact]
    public async Task ExecuteAsync_SameIdempotencyKeyTwice_CreditsCoinsOnce()
    {
        var options = new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"quest-claim-{Guid.NewGuid()}")
            .Options;
        await using var dbContext = new MergeGameDbContext(options);
        var now = new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), now.UtcDateTime);
        var economy = PlayerEconomy.CreateInitial(player.Id, now.UtcDateTime);
        var quest = PlayerQuest.CreateFirstMergeQuest(player.Id);
        quest.RecordSuccessfulMerge(now.UtcDateTime);
        quest.RecordSuccessfulMerge(now.UtcDateTime);
        quest.RecordSuccessfulMerge(now.UtcDateTime);
        dbContext.AddRange(player, economy, quest);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = new ClaimQuestRewardService(
            dbContext,
            new StubTimeProvider(now));
        var first = await service.ExecuteAsync(
            player.Id,
            PlayerQuest.FirstMergeQuestId,
            "request-001",
            expectedQuestRevision: 4,
            expectedEconomyRevision: 1);
        var second = await service.ExecuteAsync(
            player.Id,
            PlayerQuest.FirstMergeQuestId,
            "request-001",
            expectedQuestRevision: 4,
            expectedEconomyRevision: 1);

        dbContext.ChangeTracker.Clear();
        var savedEconomy = await dbContext.PlayerEconomies.SingleAsync();
        Assert.Equal(QuestRewardStatus.Succeeded, first.Status);
        Assert.Equal(QuestRewardStatus.Replayed, second.Status);
        Assert.Equal(PlayerQuest.FirstMergeRewardCoins, savedEconomy.Coins);
        Assert.Equal(1, await dbContext.RewardClaims.CountAsync());
        var ledger = Assert.Single(await dbContext.EconomyLedgerEntries.ToListAsync());
        Assert.Equal("quest.reward_claimed", ledger.Reason);
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public StubTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
