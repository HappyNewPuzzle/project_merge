namespace MergeGame.Server.Domain.Quests;

/// <summary>
/// 같은 멱등성 키의 보상이 두 번 지급되지 않았음을 증명하는 영구 원장 행입니다.
/// </summary>
public sealed class RewardClaim
{
    private RewardClaim()
    {
    }

    public Guid PlayerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string QuestId { get; private set; } = string.Empty;
    public long RewardCoins { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static RewardClaim Create(
        Guid playerId,
        string idempotencyKey,
        string questId,
        long rewardCoins,
        DateTime createdAtUtc) => new()
        {
            PlayerId = playerId,
            IdempotencyKey = idempotencyKey,
            QuestId = questId,
            RewardCoins = rewardCoins,
            CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)
        };
}
