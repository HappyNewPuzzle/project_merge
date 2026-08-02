using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Administration;

/// <summary>운영 진단에 필요한 공개 상태만 조합하고 인증 비밀과 토큰 해시는 반환하지 않습니다.</summary>
public sealed class GetAdminPlayerSummaryService
{
    private readonly MergeGameDbContext _db; private readonly TimeProvider _time;
    public GetAdminPlayerSummaryService(MergeGameDbContext db, TimeProvider time) { _db = db; _time = time; }

    public async Task<AdminPlayerSummary?> ExecuteAsync(Guid playerId, CancellationToken token = default)
    {
        var player = await _db.Players.AsNoTracking().SingleOrDefaultAsync(x => x.Id == playerId, token);
        if (player is null) return null;
        var economy = await _db.PlayerEconomies.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == playerId, token);
        var board = await _db.PlayerBoards.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == playerId, token);
        var itemCount = board is null ? 0 : await _db.BoardItems.CountAsync(x => x.PlayerId == playerId, token);
        var friendCount = await _db.Friendships.CountAsync(x => x.PlayerLowId == playerId || x.PlayerHighId == playerId, token);
        var now = _time.GetUtcNow().UtcDateTime;
        var activeSessions = await _db.RefreshTokenSessions.CountAsync(x => x.PlayerId == playerId
            && x.RevokedAtUtc == null && x.ExpiresAtUtc > now, token);
        var moderation = await _db.PlayerModerations.AsNoTracking().SingleOrDefaultAsync(x => x.PlayerId == playerId, token);
        return new AdminPlayerSummary(player.Id, player.DisplayName, player.CreatedAtUtc,
            economy?.Energy, economy?.Coins, economy?.Revision, board?.Revision, itemCount, friendCount, activeSessions,
            moderation?.IsSuspended ?? false, moderation?.Reason, moderation?.Revision ?? 0);
    }
}

/// <summary>민감한 개별 값 없이 운영 용량 판단용 서버 집계만 반환합니다.</summary>
public sealed class GetAdminOverviewService
{
    private readonly MergeGameDbContext _db; private readonly TimeProvider _time;
    public GetAdminOverviewService(MergeGameDbContext db, TimeProvider time) { _db = db; _time = time; }
    public async Task<AdminOverview> ExecuteAsync(CancellationToken token = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return new AdminOverview(
            await _db.Players.CountAsync(token),
            await _db.RefreshTokenSessions.CountAsync(x => x.RevokedAtUtc == null && x.ExpiresAtUtc > now, token),
            await _db.Friendships.CountAsync(token),
            await _db.EnergyGifts.CountAsync(x => x.GiftDateUtc == now.Date, token),
            now);
    }
}

public sealed record AdminPlayerSummary(Guid PlayerId, string DisplayName, DateTime CreatedAtUtc,
    int? Energy, long? Coins, long? EconomyRevision, long? BoardRevision, int BoardItemCount,
    int FriendCount, int ActiveRefreshSessionCount, bool IsSuspended, string? SuspensionReason, long ModerationRevision);
public sealed record AdminOverview(int PlayerCount, int ActiveRefreshSessionCount, int FriendshipCount,
    int EnergyGiftsSentToday, DateTime ServerTimeUtc);
