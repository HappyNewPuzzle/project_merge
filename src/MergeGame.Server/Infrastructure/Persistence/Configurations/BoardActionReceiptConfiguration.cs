using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>통합 보드 액션의 최초 성공 응답과 플레이어 단위 멱등 키를 MySQL에 매핑합니다.</summary>
public sealed class BoardActionReceiptConfiguration : IEntityTypeConfiguration<BoardActionReceipt>
{
    public void Configure(EntityTypeBuilder<BoardActionReceipt> builder)
    {
        builder.ToTable("board_action_receipts");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").HasColumnType("char(36)");
        builder.Property(value => value.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(value => value.IdempotencyKey).HasColumnName("idempotency_key")
            .HasMaxLength(64).UseCollation("ascii_bin");
        builder.Property(value => value.SourceSlot).HasColumnName("source_slot");
        builder.Property(value => value.TargetSlot).HasColumnName("target_slot");
        builder.Property(value => value.ResponseJson).HasColumnName("response_json").HasColumnType("longtext");
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        builder.HasIndex(value => new { value.PlayerId, value.IdempotencyKey })
            .IsUnique().HasDatabaseName("ux_board_action_receipts_player_idempotency");
        builder.HasOne<Player>().WithMany().HasForeignKey(value => value.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
