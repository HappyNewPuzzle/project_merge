using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Infrastructure.Persistence;

/// <summary>
/// MySQL 및 EF Core 관련 서비스를 DI 컨테이너에 등록합니다.
/// </summary>
public static class PersistenceServiceExtensions
{
    private const string ConnectionStringName = "MergeGameDatabase";

    /// <summary>
    /// 머지 게임 DbContext와 데이터베이스 헬스 체크를 등록합니다.
    /// </summary>
    /// <param name="services">애플리케이션 서비스 컬렉션입니다.</param>
    /// <param name="configuration">설정 파일과 환경 변수가 합쳐진 구성 객체입니다.</param>
    /// <returns>다른 서비스 등록을 이어갈 수 있도록 서비스 컬렉션을 반환합니다.</returns>
    /// <exception cref="InvalidOperationException">
    /// MergeGameDatabase 연결 문자열이 누락되었을 때 발생합니다.
    /// 잘못된 설정으로 서버가 실행된 뒤 요청 시점에 실패하는 것보다 시작 시 즉시 원인을 알리는 편이 안전합니다.
    /// </exception>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} 설정이 필요합니다.");

        services.AddDbContext<MergeGameDbContext>(options =>
        {
            // 명시한 서버 버전을 기준으로 SQL을 생성하므로 개발/운영 환경의 MySQL 버전을 동일하게 유지해야 합니다.
            // 자동 감지는 서버 시작마다 DB 연결을 요구하므로 명시적 버전이 배포 예측 가능성이 더 높습니다.
            var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
            options.UseMySql(connectionString, serverVersion);
        });

        // 전용 검사 클래스가 DbContext를 이용해 실제 MySQL 연결 가능 여부를 확인합니다.
        // /health 호출 시 MySQL에 연결할 수 없으면 비정상 상태(HTTP 503)가 됩니다.
        services.AddHealthChecks()
            .AddCheck<MySqlHealthCheck>("mysql");

        return services;
    }
}
