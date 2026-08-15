using MergeGame.Server.Domain.Quests;

namespace MergeGame.Server.Tests.Domain.Quests;

/// <summary>머지 퀘스트 완료와 보상 상태 전이를 검증합니다.</summary>
public sealed class PlayerQuestTests
{
    [Fact]
    public void RecordThreeMerges_CompletesQuestAndAllowsSingleClaim()
    {
        var quest = PlayerQuest.CreateFirstMergeQuest(Guid.NewGuid());
        var now = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc);

        quest.RecordSuccessfulMerge(now);
        quest.RecordSuccessfulMerge(now);
        quest.RecordSuccessfulMerge(now);
        var claim = quest.TryMarkClaimed(expectedRevision: 4, now);
        var secondClaim = quest.TryMarkClaimed(expectedRevision: 5, now);
        var snapshot = quest.ToSnapshot();

        Assert.Equal(QuestClaimError.None, claim);
        Assert.Equal(QuestClaimError.AlreadyClaimed, secondClaim);
        Assert.Equal(3, snapshot.CurrentCount);
        Assert.True(snapshot.IsCompleted);
        Assert.True(snapshot.IsClaimed);
        Assert.Equal(5, snapshot.Revision);
    }

    [Fact]
    public void ClaimBeforeCompletion_IsRejectedWithoutRevisionChange()
    {
        var quest = PlayerQuest.CreateFirstMergeQuest(Guid.NewGuid());

        var result = quest.TryMarkClaimed(
            expectedRevision: 1,
            DateTime.UtcNow);

        Assert.Equal(QuestClaimError.NotCompleted, result);
        Assert.Equal(1, quest.Revision);
    }

    [Fact]
    public void EnsureCurrentPeriod_NewDailyKey_ResetsProgressAndClaimState()
    {
        var definition = new QuestDefinition(
            "daily_test", "item_generated", 1, 10, QuestPeriodType.Daily);
        var quest = PlayerQuest.Create(Guid.NewGuid(), definition, "2026-08-14");
        quest.RecordEvent("item_generated", new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(QuestClaimError.None, quest.TryMarkClaimed(2, DateTime.UtcNow));

        var changed = quest.EnsureCurrentPeriod(definition, "2026-08-15");
        var snapshot = quest.ToSnapshot();

        Assert.True(changed);
        Assert.Equal(0, snapshot.CurrentCount);
        Assert.False(snapshot.IsCompleted);
        Assert.False(snapshot.IsClaimed);
        Assert.Equal("2026-08-15", snapshot.PeriodKey);
    }

    [Fact]
    public void WeeklyPeriodKey_UsesUtcMonday()
    {
        var sunday = new DateTime(2026, 8, 16, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal("2026-08-10", QuestPeriodKey.Create(QuestPeriodType.Weekly, sunday));
        Assert.Equal("2026-08-17", QuestPeriodKey.Create(QuestPeriodType.Weekly, sunday.AddHours(1)));
    }
}
