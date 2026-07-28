using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>
/// Player 도메인 엔티티를 MySQL의 players 테이블에 매핑합니다.
/// DB 제약 조건을 코드로 관리해 모든 환경에 동일한 스키마를 재현합니다.
/// </summary>
public sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("players");

        builder.HasKey(player => player.Id);

        builder.Property(player => player.Id)
            .HasColumnName("id")
            .HasColumnType("char(36)")
            .IsRequired();

        builder.Property(player => player.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(player => player.GuestTokenHash)
            .HasColumnName("guest_token_hash")
            .HasColumnType("char(64)")
            .UseCollation("ascii_bin")
            .IsRequired();

        builder.Property(player => player.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 인증 시 토큰 해시로 한 명을 빠르게 찾고, 극히 드문 중복도 DB 수준에서 차단합니다.
        builder.HasIndex(player => player.GuestTokenHash)
            .IsUnique()
            .HasDatabaseName("ux_players_guest_token_hash");
    }
}
