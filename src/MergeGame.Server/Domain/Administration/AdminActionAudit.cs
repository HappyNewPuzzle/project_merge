namespace MergeGame.Server.Domain.Administration;

/// <summary>관리자 변경의 이유와 결과를 멱등성 키와 함께 영구 보존하는 감사 원장입니다.</summary>
public sealed class AdminActionAudit
{
    private AdminActionAudit() { }
    public Guid Id { get; private set; }
    public string OperatorId { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    public Guid TargetPlayerId { get; private set; }
    public string Action { get; private set; } = "";
    public string BeforeValue { get; private set; } = "";
    public string AfterValue { get; private set; } = "";
    public string Reason { get; private set; } = "";
    public long ResultRevision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static AdminActionAudit Create(string operatorId, string key, Guid playerId, bool before, bool after,
        string reason, long revision, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(), OperatorId = operatorId, IdempotencyKey = key, TargetPlayerId = playerId,
        Action = "player.suspension.changed", BeforeValue = before ? "suspended" : "active",
        AfterValue = after ? "suspended" : "active", Reason = reason, ResultRevision = revision, CreatedAtUtc = nowUtc
    };
}
