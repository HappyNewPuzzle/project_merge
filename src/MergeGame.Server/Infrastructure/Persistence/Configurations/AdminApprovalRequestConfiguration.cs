using MergeGame.Server.Domain.Administration;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

public sealed class AdminApprovalRequestConfiguration : IEntityTypeConfiguration<AdminApprovalRequest>
{
    public void Configure(EntityTypeBuilder<AdminApprovalRequest> b)
    {
        b.ToTable("admin_approval_requests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("char(36)");
        b.Property(x => x.RequestedBy).HasColumnName("requested_by").HasMaxLength(64).UseCollation("ascii_bin");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64).UseCollation("ascii_bin");
        b.Property(x => x.TargetPlayerId).HasColumnName("target_player_id").HasColumnType("char(36)");
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("bigint");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(256);
        b.Property(x => x.ExpectedEconomyRevision).HasColumnName("expected_economy_revision").HasColumnType("bigint");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).UseCollation("ascii_bin");
        b.Property(x => x.ApprovedBy).HasColumnName("approved_by").HasMaxLength(64).UseCollation("ascii_bin");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        b.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("datetime(6)");
        b.Property(x => x.ApprovedAtUtc).HasColumnName("approved_at_utc").HasColumnType("datetime(6)");
        b.HasIndex(x => new { x.RequestedBy, x.IdempotencyKey }).IsUnique()
            .HasDatabaseName("ux_admin_approvals_requester_idempotency");
        b.HasOne<Player>().WithMany().HasForeignKey(x => x.TargetPlayerId).OnDelete(DeleteBehavior.Restrict);
    }
}
