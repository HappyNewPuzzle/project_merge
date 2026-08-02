using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>
/// 보안 분석에 필요한 기간이 지난 만료·폐기 session을 제한된 배치로 삭제합니다.
/// 한 번에 전체 테이블을 삭제하지 않아 MySQL 잠금과 트랜잭션 로그 급증을 방지합니다.
/// </summary>
public sealed class RefreshSessionCleanupService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly RefreshSessionCleanupOptions _options;
    private readonly TimeProvider _timeProvider;

    public RefreshSessionCleanupService(
        MergeGameDbContext dbContext,
        RefreshSessionCleanupOptions options,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>현재 UTC 시각의 보존 경계보다 오래된 행을 설정된 최대 배치만큼 정리합니다.</summary>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-_options.RetentionDays);
        var deletedCount = 0;

        for (var batch = 0; batch < _options.MaxBatchesPerRun; batch++)
        {
            var sessions = await _dbContext.RefreshTokenSessions
                .Where(session => session.ExpiresAtUtc <= cutoffUtc
                    || (session.RevokedAtUtc != null && session.RevokedAtUtc <= cutoffUtc))
                .OrderBy(session => session.ExpiresAtUtc)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);
            if (sessions.Count == 0) break;

            _dbContext.RefreshTokenSessions.RemoveRange(sessions);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // 여러 서버 인스턴스가 같은 행을 먼저 삭제한 경우 실패로 보지 않고 다음 배치를 조회합니다.
                _dbContext.ChangeTracker.Clear();
                continue;
            }
            deletedCount += sessions.Count;

            // 마지막 부분 배치라면 추가 조회 없이 이번 실행을 종료합니다.
            if (sessions.Count < _options.BatchSize) break;
        }

        return deletedCount;
    }
}
