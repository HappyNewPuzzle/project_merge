using MergeGame.Server.Domain.Administration;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Administration;

/// <summary>고액 코인 조정 승인 요청을 멱등 생성합니다.</summary>
public sealed class CreateCoinAdjustmentApprovalService
{
    private readonly MergeGameDbContext _db; private readonly TimeProvider _time;
    public CreateCoinAdjustmentApprovalService(MergeGameDbContext db, TimeProvider time) { _db = db; _time = time; }

    public async Task<AdminApprovalState?> ExecuteAsync(
        string requester, string key, Guid playerId, long amount, string reason, long revision,
        CancellationToken token = default)
    {
        var existing = await _db.AdminApprovalRequests.AsNoTracking().SingleOrDefaultAsync(
            x => x.RequestedBy == requester && x.IdempotencyKey == key, token);
        if (existing is not null) return Map(existing);
        if (!await _db.Players.AnyAsync(x => x.Id == playerId, token)) return null;
        var request = AdminApprovalRequest.CreateCoinAdjustment(
            requester, key, playerId, amount, reason, revision, _time.GetUtcNow().UtcDateTime);
        _db.AdminApprovalRequests.Add(request);
        try { await _db.SaveChangesAsync(token); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            request = await _db.AdminApprovalRequests.SingleAsync(
                x => x.RequestedBy == requester && x.IdempotencyKey == key, token);
        }
        return Map(request);
    }

    internal static AdminApprovalState Map(AdminApprovalRequest x) => new(
        x.Id, x.RequestedBy, x.TargetPlayerId, x.Amount, x.Reason, x.ExpectedEconomyRevision,
        x.Status, x.ApprovedBy, x.CreatedAtUtc, x.ExpiresAtUtc, x.ApprovedAtUtc);
}

/// <summary>다른 운영자의 승인을 기록하고 기존 감사·원장 기반 코인 조정을 실행합니다.</summary>
public sealed class ApproveCoinAdjustmentService
{
    private readonly MergeGameDbContext _db; private readonly TimeProvider _time;
    private readonly AdjustPlayerCoinsService _adjust;
    public ApproveCoinAdjustmentService(MergeGameDbContext db, TimeProvider time, AdjustPlayerCoinsService adjust)
    { _db = db; _time = time; _adjust = adjust; }

    public async Task<ApprovalExecutionResult> ExecuteAsync(Guid approvalId, string approver, CancellationToken token = default)
    {
        var request = await _db.AdminApprovalRequests.SingleOrDefaultAsync(x => x.Id == approvalId, token);
        if (request is null) return new(ApprovalExecutionStatus.NotFound, null, null);
        if (request.Status == "approved")
            return new(ApprovalExecutionStatus.Replayed, CreateCoinAdjustmentApprovalService.Map(request), null);
        var transition = request.TryApprove(approver, _time.GetUtcNow().UtcDateTime);
        if (transition != ApprovalTransitionError.None)
            return new(transition == ApprovalTransitionError.SameOperator
                ? ApprovalExecutionStatus.SameOperator
                : ApprovalExecutionStatus.Invalid, CreateCoinAdjustmentApprovalService.Map(request), null);

        var adjustment = await _adjust.ExecuteAsync(
            request.TargetPlayerId, approver, $"approval-{request.Id:N}", request.Amount,
            request.Reason, request.ExpectedEconomyRevision, token);
        if (adjustment.Status is not (CoinAdjustmentStatus.Succeeded or CoinAdjustmentStatus.Replayed))
            return new(ApprovalExecutionStatus.AdjustmentFailed, null, adjustment);
        return new(ApprovalExecutionStatus.Succeeded, CreateCoinAdjustmentApprovalService.Map(request), adjustment);
    }
}

public sealed record AdminApprovalState(
    Guid ApprovalId, string RequestedBy, Guid TargetPlayerId, long Amount, string Reason,
    long ExpectedEconomyRevision, string Status, string? ApprovedBy,
    DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime? ApprovedAtUtc);
public enum ApprovalExecutionStatus { Succeeded, Replayed, NotFound, SameOperator, Invalid, AdjustmentFailed }
public sealed record ApprovalExecutionResult(
    ApprovalExecutionStatus Status, AdminApprovalState? Approval, CoinAdjustmentResult? Adjustment);
