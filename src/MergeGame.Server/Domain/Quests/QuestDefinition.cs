namespace MergeGame.Server.Domain.Quests;

/// <summary>서버가 허용하는 퀘스트 목표, 기간과 보상 규칙입니다.</summary>
public sealed record QuestDefinition(
    string QuestId,
    string EventType,
    int TargetCount,
    long RewardCoins,
    QuestPeriodType PeriodType);

public enum QuestPeriodType { Lifetime, Daily, Weekly }

public interface IQuestCatalog
{
    IReadOnlyList<QuestDefinition> GetAll();
    bool TryGet(string questId, out QuestDefinition definition);
}

/// <summary>UTC 시각을 일일·주간 퀘스트 인스턴스의 안정된 기간 키로 변환합니다.</summary>
public static class QuestPeriodKey
{
    public static string Create(QuestPeriodType periodType, DateTime nowUtc)
    {
        var utc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        return periodType switch
        {
            QuestPeriodType.Lifetime => "lifetime",
            QuestPeriodType.Daily => utc.ToString("yyyy-MM-dd"),
            QuestPeriodType.Weekly => StartOfWeek(utc).ToString("yyyy-MM-dd"),
            _ => throw new ArgumentOutOfRangeException(nameof(periodType))
        };
    }

    private static DateTime StartOfWeek(DateTime utc)
    {
        var daysFromMonday = ((int)utc.DayOfWeek + 6) % 7;
        return utc.Date.AddDays(-daysFromMonday);
    }
}
