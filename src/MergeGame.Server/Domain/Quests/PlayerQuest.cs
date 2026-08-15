namespace MergeGame.Server.Domain.Quests;

/// <summary>
/// 플레이어별 퀘스트 진행도와 보상 수령 상태를 관리합니다.
/// </summary>
public sealed class PlayerQuest
{
    public const string FirstMergeQuestId = "merge_3";
    public const int FirstMergeTarget = 3;
    public const long FirstMergeRewardCoins = 100;

    private PlayerQuest()
    {
    }

    public Guid PlayerId { get; private set; }
    public string QuestId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string PeriodType { get; private set; } = string.Empty;
    public string PeriodKey { get; private set; } = string.Empty;
    public int CurrentCount { get; private set; }
    public int TargetCount { get; private set; }
    public long RewardCoins { get; private set; }
    public long Revision { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? ClaimedAtUtc { get; private set; }

    public static PlayerQuest CreateFirstMergeQuest(Guid playerId)
    {
        return Create(
            playerId,
            new QuestDefinition(
                FirstMergeQuestId,
                "item_merged",
                FirstMergeTarget,
                FirstMergeRewardCoins,
                QuestPeriodType.Lifetime),
            "lifetime");
    }

    public static PlayerQuest Create(Guid playerId, QuestDefinition definition, string periodKey)
    {
        return new PlayerQuest
        {
            PlayerId = playerId,
            QuestId = definition.QuestId,
            EventType = definition.EventType,
            PeriodType = definition.PeriodType.ToString().ToLowerInvariant(),
            PeriodKey = periodKey,
            TargetCount = definition.TargetCount,
            RewardCoins = definition.RewardCoins,
            Revision = 1
        };
    }

    /// <summary>일일·주간 경계가 바뀌면 같은 퀘스트 행을 새 기간의 초기 상태로 전환합니다.</summary>
    public bool EnsureCurrentPeriod(QuestDefinition definition, string periodKey)
    {
        if (string.Equals(PeriodKey, periodKey, StringComparison.Ordinal)
            && string.Equals(EventType, definition.EventType, StringComparison.Ordinal)
            && TargetCount == definition.TargetCount
            && RewardCoins == definition.RewardCoins)
            return false;

        EventType = definition.EventType;
        PeriodType = definition.PeriodType.ToString().ToLowerInvariant();
        PeriodKey = periodKey;
        CurrentCount = 0;
        TargetCount = definition.TargetCount;
        RewardCoins = definition.RewardCoins;
        CompletedAtUtc = null;
        ClaimedAtUtc = null;
        Revision++;
        return true;
    }

    /// <summary>
    /// 성공한 머지 한 건을 반영하며 목표 이상으로 카운트가 증가하지 않게 합니다.
    /// </summary>
    public void RecordSuccessfulMerge(DateTime occurredAtUtc)
        => RecordEvent("item_merged", occurredAtUtc);

    /// <summary>이 퀘스트가 구독한 서버 확정 이벤트만 목표 수치까지 반영합니다.</summary>
    public void RecordEvent(string eventType, DateTime occurredAtUtc)
    {
        if (!string.Equals(EventType, eventType, StringComparison.Ordinal) || CompletedAtUtc is not null)
        {
            return;
        }

        CurrentCount++;
        Revision++;
        if (CurrentCount >= TargetCount)
        {
            CurrentCount = TargetCount;
            CompletedAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// 완료·미수령·revision 조건을 검증한 뒤 보상을 수령 상태로 변경합니다.
    /// </summary>
    public QuestClaimError TryMarkClaimed(long expectedRevision, DateTime claimedAtUtc)
    {
        if (Revision != expectedRevision)
        {
            return QuestClaimError.StaleRevision;
        }
        if (CompletedAtUtc is null)
        {
            return QuestClaimError.NotCompleted;
        }
        if (ClaimedAtUtc is not null)
        {
            return QuestClaimError.AlreadyClaimed;
        }

        ClaimedAtUtc = DateTime.SpecifyKind(claimedAtUtc, DateTimeKind.Utc);
        Revision++;
        return QuestClaimError.None;
    }

    public QuestSnapshot ToSnapshot() => new(
        QuestId,
        CurrentCount,
        TargetCount,
        RewardCoins,
        Revision,
        CompletedAtUtc is not null,
        ClaimedAtUtc is not null,
        EventType,
        PeriodType,
        PeriodKey);
}

public enum QuestClaimError
{
    None,
    StaleRevision,
    NotCompleted,
    AlreadyClaimed,
    StaleEconomyRevision
}

public sealed record QuestSnapshot(
    string QuestId,
    int CurrentCount,
    int TargetCount,
    long RewardCoins,
    long Revision,
    bool IsCompleted,
    bool IsClaimed,
    string EventType,
    string PeriodType,
    string PeriodKey);
