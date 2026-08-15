using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>불변 경제 원장과 플레이어별 시간순 조회 인덱스를 MySQL에 매핑합니다.</summary>
public sealed class EconomyLedgerEntryConfiguration : IEntityTypeConfiguration<EconomyLedgerEntry>
{
    public void Configure(EntityTypeBuilder<EconomyLedgerEntry> builder)
    {
        builder.ToTable("economy_ledger_entries", table =>
            table.HasCheckConstraint("ck_economy_ledger_nonzero_delta", "`delta` <> 0"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").HasColumnType("char(36)");
        builder.Property(value => value.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(value => value.Resource).HasColumnName("resource").HasMaxLength(16).UseCollation("ascii_bin");
        builder.Property(value => value.Reason).HasColumnName("reason").HasMaxLength(64).UseCollation("ascii_bin");
        builder.Property(value => value.Delta).HasColumnName("delta").HasColumnType("bigint");
        builder.Property(value => value.BalanceAfter).HasColumnName("balance_after").HasColumnType("bigint");
        builder.Property(value => value.EconomyRevision).HasColumnName("economy_revision").HasColumnType("bigint");
        builder.Property(value => value.ReferenceId).HasColumnName("reference_id").HasMaxLength(128).UseCollation("ascii_bin");
        builder.Property(value => value.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("datetime(6)");
        builder.HasIndex(value => new { value.PlayerId, value.OccurredAtUtc })
            .HasDatabaseName("ix_economy_ledger_player_occurred");
        builder.HasOne<Player>().WithMany().HasForeignKey(value => value.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
