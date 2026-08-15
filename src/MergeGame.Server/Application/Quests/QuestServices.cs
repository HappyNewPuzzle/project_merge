using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Quests;

/// <summary>첫 머지 퀘스트를 중복 없이 초기화하고 조회합니다.</summary>
public sealed class QuestQueryService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IQuestCatalog _questCatalog;
    private readonly TimeProvider _timeProvider;

    public QuestQueryService(
        MergeGameDbContext dbContext,
        IQuestCatalog questCatalog,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _questCatalog = questCatalog;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<QuestSnapshot>?> InitializeAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Players.AnyAsync(value => value.Id == playerId, cancellationToken))
            return null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var quests = await _dbContext.PlayerQuests
            .Where(value => value.PlayerId == playerId)
            .ToListAsync(cancellationToken);
        foreach (var definition in _questCatalog.GetAll())
        {
            var periodKey = QuestPeriodKey.Create(definition.PeriodType, now);
            var quest = quests.SingleOrDefault(value => value.QuestId == definition.QuestId);
            if (quest is null)
            {
                quest = PlayerQuest.Create(playerId, definition, periodKey);
                quests.Add(quest);
                _dbContext.PlayerQuests.Add(quest);
            }
            else
            {
                quest.EnsureCurrentPeriod(definition, periodKey);
            }
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            quests = await _dbContext.PlayerQuests.AsNoTracking()
                .Where(value => value.PlayerId == playerId)
                .ToListAsync(cancellationToken);
        }
        return quests.OrderBy(value => value.QuestId, StringComparer.Ordinal)
            .Select(value => value.ToSnapshot()).ToArray();
    }

    public Task<IReadOnlyList<QuestSnapshot>?> GetAsync(
        Guid playerId,
        CancellationToken cancellationToken = default) => InitializeAsync(playerId, cancellationToken);
}

/// <summary>퀘스트 보상, 경제 코인, 멱등성 원장을 한 트랜잭션으로 저장합니다.</summary>
public sealed class ClaimQuestRewardService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IQuestCatalog _questCatalog;

    public ClaimQuestRewardService(
        MergeGameDbContext dbContext,
        TimeProvider timeProvider,
        IQuestCatalog questCatalog)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _questCatalog = questCatalog;
    }

    public async Task<QuestRewardResult> ExecuteAsync(
        Guid playerId,
        string questId,
        string idempotencyKey,
        long expectedQuestRevision,
        long expectedEconomyRevision,
        CancellationToken cancellationToken = default)
    {
        var existingClaim = await _dbContext.RewardClaims.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.PlayerId == playerId
                    && value.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existingClaim is not null)
        {
            // 동일 키 재시도는 전달된 오래된 revision과 무관하게 최초 성공 결과를 재구성합니다.
            return await BuildCurrentResultAsync(
                playerId,
                existingClaim.QuestId,
                QuestRewardStatus.Replayed,
                cancellationToken);
        }

        var quest = await _dbContext.PlayerQuests.SingleOrDefaultAsync(
            value => value.PlayerId == playerId && value.QuestId == questId,
            cancellationToken);
        var economy = await _dbContext.PlayerEconomies.SingleOrDefaultAsync(
            value => value.PlayerId == playerId,
            cancellationToken);
        if (quest is null || economy is null)
        {
            return new QuestRewardResult(QuestRewardStatus.NotFound, null, null);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!_questCatalog.TryGet(questId, out var definition))
            return new QuestRewardResult(QuestRewardStatus.NotFound, null, economy.CreateSnapshot(now));
        quest.EnsureCurrentPeriod(
            definition,
            QuestPeriodKey.Create(definition.PeriodType, now));
        var questError = quest.TryMarkClaimed(expectedQuestRevision, now);
        if (questError != QuestClaimError.None)
        {
            return new QuestRewardResult(
                questError == QuestClaimError.StaleRevision
                    ? QuestRewardStatus.Conflict
                    : QuestRewardStatus.Invalid,
                quest.ToSnapshot(),
                economy.CreateSnapshot(now),
                questError);
        }

        if (economy.TryCreditCoins(expectedEconomyRevision, quest.RewardCoins)
            == EconomyActionError.StaleRevision)
        {
            return new QuestRewardResult(
                QuestRewardStatus.Conflict,
                quest.ToSnapshot(),
                economy.CreateSnapshot(now),
                QuestClaimError.StaleEconomyRevision);
        }

        _dbContext.RewardClaims.Add(RewardClaim.Create(
            playerId,
            idempotencyKey,
            questId,
            quest.RewardCoins,
            now));
        _dbContext.EconomyLedgerEntries.Add(EconomyLedgerEntry.CreateCoins(
            playerId,
            "quest.reward_claimed",
            quest.RewardCoins,
            economy.Coins,
            economy.Revision,
            $"quest-claim:{idempotencyKey}",
            now));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            var claimWonByConcurrentRequest = await _dbContext.RewardClaims.AsNoTracking()
                .AnyAsync(
                    value => value.PlayerId == playerId
                        && value.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            return claimWonByConcurrentRequest
                ? await BuildCurrentResultAsync(playerId, questId, QuestRewardStatus.Replayed, cancellationToken)
                : new QuestRewardResult(QuestRewardStatus.Conflict, null, null);
        }

        return new QuestRewardResult(
            QuestRewardStatus.Succeeded,
            quest.ToSnapshot(),
            economy.CreateSnapshot(now));
    }

    private async Task<QuestRewardResult> BuildCurrentResultAsync(
        Guid playerId,
        string questId,
        QuestRewardStatus status,
        CancellationToken cancellationToken)
    {
        var quest = await _dbContext.PlayerQuests.AsNoTracking().SingleAsync(
            value => value.PlayerId == playerId
                && value.QuestId == questId,
            cancellationToken);
        var economy = await _dbContext.PlayerEconomies.AsNoTracking().SingleAsync(
            value => value.PlayerId == playerId,
            cancellationToken);
        return new QuestRewardResult(
            status,
            quest.ToSnapshot(),
            economy.CreateSnapshot(_timeProvider.GetUtcNow().UtcDateTime));
    }
}

public enum QuestRewardStatus
{
    Succeeded,
    Replayed,
    NotFound,
    Invalid,
    Conflict
}

public sealed record QuestRewardResult(
    QuestRewardStatus Status,
    QuestSnapshot? Quest,
    EconomySnapshot? Economy,
    QuestClaimError Error = QuestClaimError.None);
