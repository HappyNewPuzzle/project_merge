using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>같은 플레이어의 동일 정의는 한 번만 발견되도록 DB 기본 키로 보장합니다.</summary>
public sealed class ItemDiscoveryConfiguration : IEntityTypeConfiguration<ItemDiscovery>
{
    public void Configure(EntityTypeBuilder<ItemDiscovery> b)
    {
        b.ToTable("item_discoveries");
        b.HasKey(x => new { x.PlayerId, x.ChainId, x.Level });
        b.Property(x => x.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        b.Property(x => x.ChainId).HasColumnName("chain_id").HasMaxLength(32).UseCollation("ascii_bin");
        b.Property(x => x.Level).HasColumnName("level");
        b.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
