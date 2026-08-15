namespace MergeGame.Server.Domain.Administration;

/// <summary>고액 코인 조정의 요청자와 승인자를 분리하고 최종 처리 상태를 영구 보존합니다.</summary>
public sealed class AdminApprovalRequest
{
    private AdminApprovalRequest() { }
    public Guid Id { get; private set; }
    public string RequestedBy { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid TargetPlayerId { get; private set; }
    public long Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public long ExpectedEconomyRevision { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? ApprovedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }

    public static AdminApprovalRequest CreateCoinAdjustment(
        string requestedBy, string key, Guid playerId, long amount, string reason,
        long expectedRevision, DateTime nowUtc) => new()
        {
            Id = Guid.NewGuid(), RequestedBy = requestedBy, IdempotencyKey = key,
            TargetPlayerId = playerId, Amount = amount, Reason = reason,
            ExpectedEconomyRevision = expectedRevision, Status = "pending",
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            ExpiresAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).AddHours(24)
        };

    public ApprovalTransitionError TryApprove(string approver, DateTime nowUtc)
    {
        if (!string.Equals(Status, "pending", StringComparison.Ordinal))
            return ApprovalTransitionError.NotPending;
        if (string.Equals(RequestedBy, approver, StringComparison.Ordinal))
            return ApprovalTransitionError.SameOperator;
        if (nowUtc >= ExpiresAtUtc)
            return ApprovalTransitionError.Expired;
        Status = "approved";
        ApprovedBy = approver;
        ApprovedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        return ApprovalTransitionError.None;
    }
}

public enum ApprovalTransitionError { None, NotPending, SameOperator, Expired }
