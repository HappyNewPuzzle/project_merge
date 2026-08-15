using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Quests;

/// <summary>
/// 서버가 확정한 게임 이벤트를 현재 UTC 기간의 모든 구독 퀘스트에 반영합니다.
/// SaveChanges는 호출 기능이 수행하므로 원본 게임 변경과 같은 트랜잭션에 포함됩니다.
/// </summary>
public sealed class QuestProgressService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IQuestCatalog _questCatalog;

    public QuestProgressService(MergeGameDbContext dbContext, IQuestCatalog questCatalog)
    {
        _dbContext = dbContext;
        _questCatalog = questCatalog;
    }

    public async Task RecordAsync(
        Guid playerId,
        string eventType,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        var definitions = _questCatalog.GetAll()
            .Where(value => string.Equals(value.EventType, eventType, StringComparison.Ordinal))
            .ToArray();
        if (definitions.Length == 0)
            return;

        var questIds = definitions.Select(value => value.QuestId).ToArray();
        var quests = await _dbContext.PlayerQuests
            .Where(value => value.PlayerId == playerId && questIds.Contains(value.QuestId))
            .ToListAsync(cancellationToken);
        foreach (var definition in definitions)
        {
            var periodKey = QuestPeriodKey.Create(definition.PeriodType, occurredAtUtc);
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
            quest.RecordEvent(eventType, occurredAtUtc);
        }
    }
}
