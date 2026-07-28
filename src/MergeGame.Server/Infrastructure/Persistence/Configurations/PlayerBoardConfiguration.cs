using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>
/// PlayerBoard 애그리게이트를 MySQL의 player_boards 테이블에 매핑합니다.
/// </summary>
public sealed class PlayerBoardConfiguration : IEntityTypeConfiguration<PlayerBoard>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerBoard> builder)
    {
        builder.ToTable("player_boards");

        builder.HasKey(board => board.PlayerId);

        builder.Property(board => board.PlayerId)
            .HasColumnName("player_id")
            .HasColumnType("char(36)")
            .IsRequired();

        builder.Property(board => board.Revision)
            .HasColumnName("revision")
            .HasColumnType("bigint")
            // UPDATE의 WHERE 절에 기존 revision을 포함해 동시 수정 충돌을 감지합니다.
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(board => board.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("datetime(6)")
            .IsRequired();

        builder.Property(board => board.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 플레이어가 삭제되면 소유 보드도 함께 제거되어 고아 데이터가 남지 않습니다.
        builder.HasOne<Player>()
            .WithOne()
            .HasForeignKey<PlayerBoard>(board => board.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(board => board.Items)
            .WithOne()
            .HasForeignKey(item => item.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // EF가 읽기 전용 Items 속성을 우회하고 도메인의 실제 내부 목록에 데이터를 채우게 합니다.
        builder.Navigation(board => board.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
