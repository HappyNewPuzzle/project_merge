using MergeGame.Server.Domain.Boards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>
/// BoardItem 인스턴스를 MySQL의 board_items 테이블과 슬롯 제약 조건에 매핑합니다.
/// </summary>
public sealed class BoardItemConfiguration : IEntityTypeConfiguration<BoardItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BoardItem> builder)
    {
        builder.ToTable(
            "board_items",
            tableBuilder => tableBuilder.HasCheckConstraint(
                "ck_board_items_slot_index",
                "`slot_index` >= 0 AND `slot_index` < 35"));

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasColumnType("char(36)")
            .IsRequired();

        builder.Property(item => item.PlayerId)
            .HasColumnName("player_id")
            .HasColumnType("char(36)")
            .IsRequired();

        builder.Property(item => item.SlotIndex)
            .HasColumnName("slot_index")
            .IsRequired();

        builder.Property(item => item.ChainId)
            .HasColumnName("chain_id")
            .HasMaxLength(32)
            .UseCollation("ascii_bin")
            .IsRequired();

        builder.Property(item => item.Level)
            .HasColumnName("level")
            .IsRequired();

        // 한 플레이어 보드의 한 슬롯에는 최대 한 아이템만 존재할 수 있습니다.
        builder.HasIndex(item => new { item.PlayerId, item.SlotIndex })
            .IsUnique()
            .HasDatabaseName("ux_board_items_player_slot");
    }
}
