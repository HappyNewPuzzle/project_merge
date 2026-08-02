namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>정리 설정을 시작 시 검증하고 서비스와 worker를 함께 등록합니다.</summary>
public static class RefreshSessionCleanupExtensions
{
    public static IServiceCollection AddRefreshSessionCleanup(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RefreshSessionCleanupOptions.SectionName)
            .Get<RefreshSessionCleanupOptions>() ?? new RefreshSessionCleanupOptions();
        if (options.IntervalMinutes is < 1 or > 1440) throw new InvalidOperationException("정리 주기는 1~1440분이어야 합니다.");
        if (options.RetentionDays is < 1 or > 90) throw new InvalidOperationException("보존 기간은 1~90일이어야 합니다.");
        if (options.BatchSize is < 10 or > 5000) throw new InvalidOperationException("배치 크기는 10~5000이어야 합니다.");
        if (options.MaxBatchesPerRun is < 1 or > 100) throw new InvalidOperationException("실행당 배치는 1~100회여야 합니다.");

        services.AddSingleton(options);
        services.AddScoped<RefreshSessionCleanupService>();
        services.AddHostedService<RefreshSessionCleanupWorker>();
        return services;
    }
}
