using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>아이템 판매 멱등 영수증과 플레이어 단위 고유 키를 MySQL에 매핑합니다.</summary>
public sealed class BoardItemSaleReceiptConfiguration : IEntityTypeConfiguration<BoardItemSaleReceipt>
{
    public void Configure(EntityTypeBuilder<BoardItemSaleReceipt> builder)
    {
        builder.ToTable("board_item_sale_receipts");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").HasColumnType("char(36)");
        builder.Property(value => value.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(value => value.IdempotencyKey).HasColumnName("idempotency_key")
            .HasMaxLength(64).UseCollation("ascii_bin");
        builder.Property(value => value.ItemId).HasColumnName("item_id").HasColumnType("char(36)");
        builder.Property(value => value.ResponseJson).HasColumnName("response_json").HasColumnType("longtext");
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        builder.HasIndex(value => new { value.PlayerId, value.IdempotencyKey })
            .IsUnique().HasDatabaseName("ux_board_item_sales_player_idempotency");
        builder.HasOne<Player>().WithMany().HasForeignKey(value => value.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
