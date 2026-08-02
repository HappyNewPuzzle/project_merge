using MergeGame.Server.Domain.Administration;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

public sealed class PlayerModerationConfiguration : IEntityTypeConfiguration<PlayerModeration>
{
    public void Configure(EntityTypeBuilder<PlayerModeration> b)
    {
        b.ToTable("player_moderations"); b.HasKey(x => x.PlayerId);
        b.Property(x => x.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        b.Property(x => x.IsSuspended).HasColumnName("is_suspended");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(256);
        b.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime(6)");
        b.HasOne<Player>().WithOne().HasForeignKey<PlayerModeration>(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AdminActionAuditConfiguration : IEntityTypeConfiguration<AdminActionAudit>
{
    public void Configure(EntityTypeBuilder<AdminActionAudit> b)
    {
        b.ToTable("admin_action_audits"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("char(36)");
        b.Property(x => x.OperatorId).HasColumnName("operator_id").HasMaxLength(64);
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64).UseCollation("ascii_bin");
        b.Property(x => x.TargetPlayerId).HasColumnName("target_player_id").HasColumnType("char(36)");
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(64);
        b.Property(x => x.BeforeValue).HasColumnName("before_value").HasMaxLength(32);
        b.Property(x => x.AfterValue).HasColumnName("after_value").HasMaxLength(32);
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(256);
        b.Property(x => x.ResultRevision).HasColumnName("result_revision");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        b.HasIndex(x => new { x.OperatorId, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_admin_audits_operator_idempotency");
        b.HasIndex(x => new { x.TargetPlayerId, x.CreatedAtUtc }).HasDatabaseName("ix_admin_audits_target_created");
        b.HasOne<Player>().WithMany().HasForeignKey(x => x.TargetPlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
