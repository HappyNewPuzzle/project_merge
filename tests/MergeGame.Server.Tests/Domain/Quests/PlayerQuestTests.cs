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
}
