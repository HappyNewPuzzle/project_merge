namespace MergeGame.Server.Domain.Administration;

/// <summary>플레이어의 현재 이용 정지 상태와 낙관적 동시성 revision을 보관합니다.</summary>
public sealed class PlayerModeration
{
    private PlayerModeration() { }
    public Guid PlayerId { get; private set; }
    public bool IsSuspended { get; private set; }
    public string Reason { get; private set; } = "";
    public long Revision { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static PlayerModeration Create(Guid playerId, bool suspended, string reason, DateTime nowUtc) => new()
    { PlayerId = playerId, IsSuspended = suspended, Reason = reason, Revision = 1, UpdatedAtUtc = nowUtc };

    public bool TryApply(bool suspended, string reason, long expectedRevision, DateTime nowUtc)
    {
        if (Revision != expectedRevision) return false;
        IsSuspended = suspended; Reason = reason; UpdatedAtUtc = nowUtc; Revision++;
        return true;
    }
}
