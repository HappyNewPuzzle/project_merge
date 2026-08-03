using System.Globalization;
using MergeGame.Server.Domain.Administration;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Administration;

/// <summary>코인 변경과 감사 원장을 하나의 트랜잭션으로 저장하고 재시도를 멱등 처리합니다.</summary>
public sealed class AdjustPlayerCoinsService
{
    private readonly MergeGameDbContext _db; private readonly TimeProvider _time; private readonly AdminApiOptions _options;
    public AdjustPlayerCoinsService(MergeGameDbContext db, TimeProvider time, AdminApiOptions options)
    { _db = db; _time = time; _options = options; }

    public async Task<CoinAdjustmentResult> ExecuteAsync(Guid playerId, string operatorId, string key, long amount,
        string reason, long expectedRevision, CancellationToken token = default)
    {
        var replay = await _db.AdminActionAudits.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperatorId == operatorId && x.IdempotencyKey == key, token);
        if (replay is not null) return FromAudit(replay, playerId);
        if (amount == 0 || amount == long.MinValue || Math.Abs(amount) > _options.MaxAbsoluteCoinAdjustment)
            return new(CoinAdjustmentStatus.InvalidAmount, 0, 0);

        var economy = await _db.PlayerEconomies.SingleOrDefaultAsync(x => x.PlayerId == playerId, token);
        if (economy is null) return new(CoinAdjustmentStatus.NotFound, 0, 0);
        var before = economy.Coins;
        var error = economy.TryAdjustCoins(expectedRevision, amount);
        if (error == EconomyActionError.StaleRevision)
            return new(CoinAdjustmentStatus.Conflict, economy.Coins, economy.Revision);
        if (error != EconomyActionError.None)
            return new(CoinAdjustmentStatus.InvalidBalance, economy.Coins, economy.Revision);

        var now = _time.GetUtcNow().UtcDateTime;
        _db.AdminActionAudits.Add(AdminActionAudit.CreateCoinAdjustment(operatorId, key, playerId, before,
            economy.Coins, reason, economy.Revision, now));
        try { await _db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException)
        {
            // 다른 게임/관리자 요청이 먼저 코인을 바꾼 경우 최신 상태를 반환해 운영자가 재판단하게 합니다.
            _db.ChangeTracker.Clear();
            var current = await _db.PlayerEconomies.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PlayerId == playerId, token);
            return new(CoinAdjustmentStatus.Conflict, current?.Coins ?? before, current?.Revision ?? expectedRevision);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var won = await _db.AdminActionAudits.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperatorId == operatorId && x.IdempotencyKey == key, token);
            return won is null ? new(CoinAdjustmentStatus.Conflict, before, expectedRevision) : FromAudit(won, playerId);
        }
        return new(CoinAdjustmentStatus.Succeeded, economy.Coins, economy.Revision);
    }

    private static CoinAdjustmentResult FromAudit(AdminActionAudit audit, Guid playerId)
    {
        if (audit.TargetPlayerId != playerId || audit.Action != "player.coins.adjusted"
            || !long.TryParse(audit.AfterValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var coins))
            return new(CoinAdjustmentStatus.IdempotencyConflict, 0, 0);
        return new(CoinAdjustmentStatus.Replayed, coins, audit.ResultRevision);
    }
}

public enum CoinAdjustmentStatus { Succeeded, Replayed, NotFound, Conflict, InvalidAmount, InvalidBalance, IdempotencyConflict }
public sealed record CoinAdjustmentResult(CoinAdjustmentStatus Status, long Coins, long EconomyRevision);
