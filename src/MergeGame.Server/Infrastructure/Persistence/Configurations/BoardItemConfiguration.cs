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

        // 슬롯 중복 불변식은 PlayerBoard 애그리게이트가 보장하고 보드 revision이 동시 작성을 직렬화합니다.
        // MySQL 유니크 인덱스는 두 아이템의 슬롯 교환 UPDATE 중 중간 상태를 충돌로 판단하므로 사용하지 않습니다.
    }
}
