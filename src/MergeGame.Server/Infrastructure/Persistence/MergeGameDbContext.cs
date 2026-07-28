using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Infrastructure.Persistence;

/// <summary>
/// 머지 게임 데이터베이스와 애플리케이션 사이의 작업 단위를 표현합니다.
/// 이후 플레이어, 보드, 아이템 엔티티가 추가되면 이 클래스에 DbSet을 선언합니다.
/// </summary>
public sealed class MergeGameDbContext : DbContext
{
    /// <summary>
    /// DI 컨테이너가 구성한 MySQL 연결 옵션을 받아 DbContext를 생성합니다.
    /// </summary>
    /// <param name="options">연결 문자열과 MySQL 공급자 설정이 포함된 EF Core 옵션입니다.</param>
    public MergeGameDbContext(DbContextOptions<MergeGameDbContext> options)
        : base(options)
    {
    }
}
