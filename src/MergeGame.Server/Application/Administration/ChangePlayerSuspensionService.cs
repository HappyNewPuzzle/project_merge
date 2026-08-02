using MergeGame.Server.Domain.Administration;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Administration;

/// <summary>정지 상태, 활성 refresh session 폐기와 감사 원장을 한 트랜잭션으로 저장합니다.</summary>
public sealed class ChangePlayerSuspensionService
{
    private readonly MergeGameDbContext _db; private readonly TimeProvider _time;
    public ChangePlayerSuspensionService(MergeGameDbContext db, TimeProvider time) { _db = db; _time = time; }

    public async Task<SuspensionChangeResult> ExecuteAsync(Guid playerId, string operatorId, string key,
        bool suspended, string reason, long expectedRevision, CancellationToken token = default)
    {
        var replay = await _db.AdminActionAudits.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperatorId == operatorId && x.IdempotencyKey == key, token);
        if (replay is not null)
            return replay.TargetPlayerId == playerId && replay.Action == "player.suspension.changed"
                ? new(SuspensionChangeStatus.Replayed, replay.AfterValue == "suspended", replay.ResultRevision)
                : new(SuspensionChangeStatus.IdempotencyConflict, false, 0);
        if (!await _db.Players.AnyAsync(x => x.Id == playerId, token))
            return new(SuspensionChangeStatus.NotFound, false, 0);

        var now = _time.GetUtcNow().UtcDateTime;
        var moderation = await _db.PlayerModerations.SingleOrDefaultAsync(x => x.PlayerId == playerId, token);
        var before = moderation?.IsSuspended ?? false;
        if (moderation is null)
        {
            if (expectedRevision != 0) return new(SuspensionChangeStatus.Conflict, before, 0);
            moderation = PlayerModeration.Create(playerId, suspended, reason, now);
            _db.PlayerModerations.Add(moderation);
        }
        else if (!moderation.TryApply(suspended, reason, expectedRevision, now))
            return new(SuspensionChangeStatus.Conflict, before, moderation.Revision);

        if (suspended)
        {
            var activeSessions = await _db.RefreshTokenSessions
                .Where(x => x.PlayerId == playerId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now).ToListAsync(token);
            foreach (var session in activeSessions) session.Revoke(now, "account_suspended");
        }
        _db.AdminActionAudits.Add(AdminActionAudit.Create(operatorId, key, playerId, before, suspended,
            reason, moderation.Revision, now));
        try { await _db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { return new(SuspensionChangeStatus.Conflict, before, moderation.Revision); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var won = await _db.AdminActionAudits.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperatorId == operatorId && x.IdempotencyKey == key, token);
            if (won is null) return new(SuspensionChangeStatus.Conflict, before, 0);
            return won.TargetPlayerId == playerId && won.Action == "player.suspension.changed"
                ? new(SuspensionChangeStatus.Replayed, won.AfterValue == "suspended", won.ResultRevision)
                : new(SuspensionChangeStatus.IdempotencyConflict, false, 0);
        }
        return new(SuspensionChangeStatus.Succeeded, moderation.IsSuspended, moderation.Revision);
    }
}

public enum SuspensionChangeStatus { Succeeded, Replayed, NotFound, Conflict, IdempotencyConflict }
public sealed record SuspensionChangeResult(SuspensionChangeStatus Status, bool IsSuspended, long Revision);
