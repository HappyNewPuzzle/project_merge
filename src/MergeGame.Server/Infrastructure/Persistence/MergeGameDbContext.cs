using Microsoft.EntityFrameworkCore;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Domain.Social;
using MergeGame.Server.Domain.Authentication;
using MergeGame.Server.Domain.Administration;
using MergeGame.Server.Domain.Generators;

namespace MergeGame.Server.Infrastructure.Persistence;

/// <summary>
/// 머지 게임 데이터베이스와 애플리케이션 사이의 작업 단위를 표현합니다.
/// 이후 플레이어, 보드, 아이템 엔티티가 추가되면 이 클래스에 DbSet을 선언합니다.
/// </summary>
public sealed class MergeGameDbContext : DbContext
{
    /// <summary>
    /// 게임에 가입한 플레이어를 조회하고 저장하는 컬렉션입니다.
    /// </summary>
    public DbSet<Player> Players => Set<Player>();

    /// <summary>
    /// 플레이어별 머지 보드와 보드 revision을 조회하고 저장합니다.
    /// </summary>
    public DbSet<PlayerBoard> PlayerBoards => Set<PlayerBoard>();

    /// <summary>
    /// 보드 슬롯에 배치된 개별 아이템을 조회하고 저장합니다.
    /// </summary>
    public DbSet<BoardItem> BoardItems => Set<BoardItem>();
    /// <summary>성공한 통합 보드 액션의 멱등 응답 영수증입니다.</summary>
    public DbSet<BoardActionReceipt> BoardActionReceipts => Set<BoardActionReceipt>();
    /// <summary>성공한 아이템 판매의 코인 지급을 재생하는 멱등 영수증입니다.</summary>
    public DbSet<BoardItemSaleReceipt> BoardItemSaleReceipts => Set<BoardItemSaleReceipt>();

    /// <summary>
    /// 플레이어의 에너지, 코인, 보상 이력과 경제 revision을 저장합니다.
    /// </summary>
    public DbSet<PlayerEconomy> PlayerEconomies => Set<PlayerEconomy>();
    public DbSet<PlayerQuest> PlayerQuests => Set<PlayerQuest>();
    public DbSet<GameplayEvent> GameplayEvents => Set<GameplayEvent>();
    public DbSet<RewardClaim> RewardClaims => Set<RewardClaim>();
    public DbSet<PlayerSocialProfile> PlayerSocialProfiles => Set<PlayerSocialProfile>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<EnergyGift> EnergyGifts => Set<EnergyGift>();
    public DbSet<RefreshTokenSession> RefreshTokenSessions => Set<RefreshTokenSession>();
    public DbSet<PlayerModeration> PlayerModerations => Set<PlayerModeration>();
    public DbSet<AdminActionAudit> AdminActionAudits => Set<AdminActionAudit>();
    /// <summary>플레이어별 서버 권위형 생성기의 충전 상태입니다.</summary>
    public DbSet<PlayerGenerator> PlayerGenerators => Set<PlayerGenerator>();
    /// <summary>성공한 생성 요청을 재생하는 멱등 영수증입니다.</summary>
    public DbSet<GeneratorProductionReceipt> GeneratorProductionReceipts => Set<GeneratorProductionReceipt>();

    /// <summary>
    /// DI 컨테이너가 구성한 MySQL 연결 옵션을 받아 DbContext를 생성합니다.
    /// </summary>
    /// <param name="options">연결 문자열과 MySQL 공급자 설정이 포함된 EF Core 옵션입니다.</param>
    public MergeGameDbContext(DbContextOptions<MergeGameDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// 현재 어셈블리에 정의된 엔티티별 구성 클래스를 자동으로 적용합니다.
    /// </summary>
    /// <param name="modelBuilder">EF Core 데이터베이스 모델 구성기입니다.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // IEntityTypeConfiguration 구현을 자동 탐색하므로 엔티티가 늘어나도 이 메서드를 수정할 필요가 없습니다.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MergeGameDbContext).Assembly);
    }
}
