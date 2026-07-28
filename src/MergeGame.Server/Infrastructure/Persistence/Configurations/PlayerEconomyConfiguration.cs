using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>
/// 플레이어 경제 상태, 동시성 revision, 재화 제약을 MySQL에 매핑합니다.
/// </summary>
public sealed class PlayerEconomyConfiguration : IEntityTypeConfiguration<PlayerEconomy>
{
    public void Configure(EntityTypeBuilder<PlayerEconomy> builder)
    {
        builder.ToTable(
            "player_economies",
            table =>
            {
                table.HasCheckConstraint("ck_player_economies_energy", "`energy` >= 0 AND `energy` <= 100");
                table.HasCheckConstraint("ck_player_economies_coins", "`coins` >= 0");
            });
        builder.HasKey(economy => economy.PlayerId);
        builder.Property(economy => economy.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(economy => economy.Energy).HasColumnName("energy").IsRequired();
        builder.Property(economy => economy.Coins).HasColumnName("coins").HasColumnType("bigint").IsRequired();
        builder.Property(economy => economy.Revision).HasColumnName("revision").HasColumnType("bigint").IsConcurrencyToken();
        builder.Property(economy => economy.LastEnergyUpdatedAtUtc).HasColumnName("last_energy_updated_at_utc").HasColumnType("datetime(6)");
        builder.Property(economy => economy.LastDailyRewardClaimedAtUtc).HasColumnName("last_daily_reward_claimed_at_utc").HasColumnType("datetime(6)");
        builder.HasOne<Player>().WithOne().HasForeignKey<PlayerEconomy>(economy => economy.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
