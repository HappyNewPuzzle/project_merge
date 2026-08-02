namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>refresh session 정리 주기, 보안 보존 기간과 한 번의 DB 작업량을 제어합니다.</summary>
public sealed class RefreshSessionCleanupOptions
{
    public const string SectionName = "RefreshSessionCleanup";
    public int IntervalMinutes { get; init; } = 60;
    public int RetentionDays { get; init; } = 7;
    public int BatchSize { get; init; } = 500;
    public int MaxBatchesPerRun { get; init; } = 10;
}
