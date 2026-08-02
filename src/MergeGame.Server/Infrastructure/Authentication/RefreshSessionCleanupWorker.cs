namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>서버 수명 동안 정리용 scoped DbContext를 매 주기 새로 만드는 백그라운드 작업입니다.</summary>
public sealed class RefreshSessionCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RefreshSessionCleanupOptions _options;
    private readonly ILogger<RefreshSessionCleanupWorker> _logger;

    public RefreshSessionCleanupWorker(
        IServiceScopeFactory scopeFactory,
        RefreshSessionCleanupOptions options,
        ILogger<RefreshSessionCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 시작 직후 마이그레이션·배포 트래픽과 경쟁하지 않도록 첫 정리도 한 주기 뒤 수행합니다.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<RefreshSessionCleanupService>();
                var deleted = await cleanup.ExecuteAsync(stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("Old refresh sessions cleaned. DeletedCount={DeletedCount}", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // 일시 DB 장애가 서버 프로세스를 종료하지 않게 하고 다음 주기에 다시 시도합니다.
                _logger.LogError(exception, "Refresh session cleanup failed.");
            }
        }
    }
}
