using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Economy;

/// <summary>인증 플레이어의 최근 경제 증감 이력을 최신 순으로 제한 조회합니다.</summary>
public sealed class GetEconomyLedgerService
{
    private readonly MergeGameDbContext _dbContext;
    public GetEconomyLedgerService(MergeGameDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<EconomyLedgerEntryState>> ExecuteAsync(
        Guid playerId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        return await _dbContext.EconomyLedgerEntries.AsNoTracking()
            .Where(value => value.PlayerId == playerId)
            .OrderByDescending(value => value.OccurredAtUtc)
            .ThenByDescending(value => value.Id)
            .Take(safeLimit)
            .Select(value => new EconomyLedgerEntryState(
                value.Id,
                value.Resource,
                value.Reason,
                value.Delta,
                value.BalanceAfter,
                value.EconomyRevision,
                value.ReferenceId,
                value.OccurredAtUtc))
            .ToListAsync(cancellationToken);
    }
}

public sealed record EconomyLedgerEntryState(
    Guid EntryId,
    string Resource,
    string Reason,
    long Delta,
    long BalanceAfter,
    long EconomyRevision,
    string ReferenceId,
    DateTime OccurredAtUtc);
