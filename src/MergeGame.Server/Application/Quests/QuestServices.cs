using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Quests;

/// <summary>첫 머지 퀘스트를 중복 없이 초기화하고 조회합니다.</summary>
public sealed class QuestQueryService
{
    private readonly MergeGameDbContext _dbContext;

    public QuestQueryService(MergeGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<QuestSnapshot?> InitializeAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var quest = await FindAsync(playerId, tracking: true, cancellationToken);
        if (quest is not null)
        {
            return quest.ToSnapshot();
        }
        if (!await _dbContext.Players.AnyAsync(value => value.Id == playerId, cancellationToken))
        {
            return null;
        }

        quest = PlayerQuest.CreateFirstMergeQuest(playerId);
        _dbContext.PlayerQuests.Add(quest);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            quest = await FindAsync(playerId, tracking: false, cancellationToken);
        }
        return quest?.ToSnapshot();
    }

    public async Task<QuestSnapshot?> GetAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var quest = await FindAsync(playerId, tracking: false, cancellationToken);
        return quest?.ToSnapshot();
    }

    private Task<PlayerQuest?> FindAsync(
        Guid playerId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = tracking
            ? _dbContext.PlayerQuests
            : _dbContext.PlayerQuests.AsNoTracking();
        return query.SingleOrDefaultAsync(
            value => value.PlayerId == playerId
                && value.QuestId == PlayerQuest.FirstMergeQuestId,
            cancellationToken);
    }
}

/// <summary>퀘스트 보상, 경제 코인, 멱등성 원장을 한 트랜잭션으로 저장합니다.</summary>
public sealed class ClaimQuestRewardService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ClaimQuestRewardService(
        MergeGameDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
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
                ? await BuildCurrentResultAsync(playerId, QuestRewardStatus.Replayed, cancellationToken)
                : new QuestRewardResult(QuestRewardStatus.Conflict, null, null);
        }

        return new QuestRewardResult(
            QuestRewardStatus.Succeeded,
            quest.ToSnapshot(),
            economy.CreateSnapshot(now));
    }

    private async Task<QuestRewardResult> BuildCurrentResultAsync(
        Guid playerId,
        QuestRewardStatus status,
        CancellationToken cancellationToken)
    {
        var quest = await _dbContext.PlayerQuests.AsNoTracking().SingleAsync(
            value => value.PlayerId == playerId
                && value.QuestId == PlayerQuest.FirstMergeQuestId,
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
