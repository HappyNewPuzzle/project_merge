using MergeGame.Server.Domain.Quests;

namespace MergeGame.Server.Infrastructure.Quests;

/// <summary>현재 활성 일회성·일일·주간 퀘스트 정의를 서버 코드로 버전 관리합니다.</summary>
public sealed class InMemoryQuestCatalog : IQuestCatalog
{
    private static readonly IReadOnlyList<QuestDefinition> Definitions =
    [
        new(PlayerQuest.FirstMergeQuestId, "item_merged", 3, 100, QuestPeriodType.Lifetime),
        new("daily_generate_5", "item_generated", 5, 30, QuestPeriodType.Daily),
        new("daily_sell_3", "item_sold", 3, 40, QuestPeriodType.Daily),
        new("daily_friend_gift_1", "friend_energy_sent", 1, 25, QuestPeriodType.Daily),
        new("weekly_merge_20", "item_merged", 20, 250, QuestPeriodType.Weekly)
    ];

    public IReadOnlyList<QuestDefinition> GetAll() => Definitions;

    public bool TryGet(string questId, out QuestDefinition definition)
    {
        definition = Definitions.SingleOrDefault(
            value => string.Equals(value.QuestId, questId, StringComparison.Ordinal))!;
        return definition is not null;
    }
}
