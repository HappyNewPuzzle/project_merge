using MergeGame.Server.Domain.Authentication;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>토큰 해시 고유성과 플레이어·토큰 계열 조회 인덱스를 MySQL에 정의합니다.</summary>
public sealed class RefreshTokenSessionConfiguration : IEntityTypeConfiguration<RefreshTokenSession>
{
    public void Configure(EntityTypeBuilder<RefreshTokenSession> builder)
    {
        builder.ToTable("refresh_token_sessions"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("char(36)");
        builder.Property(x => x.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(x => x.FamilyId).HasColumnName("family_id").HasColumnType("char(36)");
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasColumnType("char(64)").UseCollation("ascii_bin");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("datetime(6)");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc").HasColumnType("datetime(6)").IsConcurrencyToken();
        builder.Property(x => x.ReplacedBySessionId).HasColumnName("replaced_by_session_id").HasColumnType("char(36)");
        builder.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(32);
        builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_sessions_token_hash");
        builder.HasIndex(x => new { x.PlayerId, x.FamilyId }).HasDatabaseName("ix_refresh_sessions_player_family");
        builder.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
