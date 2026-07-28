using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MergeGame.Server.Infrastructure.Persistence;

/// <summary>
/// 애플리케이션이 사용하는 자격 증명으로 MySQL에 실제 접속할 수 있는지 검사합니다.
/// 단순히 서버 프로세스가 실행 중인지뿐 아니라 데이터 저장 기능까지 준비됐는지 확인합니다.
/// </summary>
public sealed class MySqlHealthCheck : IHealthCheck
{
    private readonly MergeGameDbContext _dbContext;

    /// <summary>
    /// 요청 범위에 맞게 생성된 DbContext를 주입받습니다.
    /// </summary>
    /// <param name="dbContext">MySQL 연결 설정을 보유한 게임 DbContext입니다.</param>
    public MySqlHealthCheck(MergeGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 짧은 연결 시도를 수행하고 결과를 ASP.NET Core 헬스 체크 형식으로 변환합니다.
    /// </summary>
    /// <param name="context">검사 실패 상태 등 등록 시점의 메타데이터입니다.</param>
    /// <param name="cancellationToken">클라이언트 연결 종료나 서버 종료 시 검사를 중단하는 토큰입니다.</param>
    /// <returns>연결 가능 여부가 담긴 헬스 체크 결과입니다.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("MySQL connection is available.")
                : HealthCheckResult.Unhealthy("MySQL connection is unavailable.");
        }
        catch (Exception exception)
        {
            // 예외를 결과에 포함하면 서버 로그와 개발 환경 진단에 도움이 됩니다.
            // 기본 /health 응답은 상세 예외를 외부에 노출하지 않아 비밀번호 등의 유출을 막습니다.
            return HealthCheckResult.Unhealthy(
                "MySQL connection check failed.",
                exception);
        }
    }
}
