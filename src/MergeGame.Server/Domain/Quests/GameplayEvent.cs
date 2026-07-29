namespace MergeGame.Server.Domain.Quests;

/// <summary>
/// 서버가 확정한 게임 행동을 감사와 후속 퀘스트 처리용으로 기록합니다.
/// </summary>
public sealed class GameplayEvent
{
    private GameplayEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public long BoardRevision { get; private set; }
    public int ResultItemLevel { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static GameplayEvent CreateMerge(
        Guid playerId,
        long boardRevision,
        int resultItemLevel,
        DateTime occurredAtUtc) => new()
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            EventType = "item_merged",
            BoardRevision = boardRevision,
            ResultItemLevel = resultItemLevel,
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc)
        };
}
