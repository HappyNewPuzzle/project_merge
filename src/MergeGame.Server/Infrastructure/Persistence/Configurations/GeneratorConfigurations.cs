using MergeGame.Server.Domain.Generators;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>플레이어별 생성기 충전 상태를 MySQL에 매핑합니다.</summary>
public sealed class PlayerGeneratorConfiguration : IEntityTypeConfiguration<PlayerGenerator>
{
    public void Configure(EntityTypeBuilder<PlayerGenerator> builder)
    {
        builder.ToTable("player_generators", table =>
            table.HasCheckConstraint("ck_player_generators_charges", "`charges` >= 0"));
        builder.HasKey(value => new { value.PlayerId, value.GeneratorId });
        builder.Property(value => value.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(value => value.GeneratorId).HasColumnName("generator_id").HasMaxLength(32).UseCollation("ascii_bin");
        builder.Property(value => value.Charges).HasColumnName("charges");
        builder.Property(value => value.Revision).HasColumnName("revision").HasColumnType("bigint").IsConcurrencyToken();
        builder.Property(value => value.ChargeUpdatedAtUtc).HasColumnName("charge_updated_at_utc").HasColumnType("datetime(6)");
        builder.HasOne<Player>().WithMany().HasForeignKey(value => value.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>성공 응답 영수증과 플레이어 단위 멱등 유니크 키를 MySQL에 매핑합니다.</summary>
public sealed class GeneratorProductionReceiptConfiguration : IEntityTypeConfiguration<GeneratorProductionReceipt>
{
    public void Configure(EntityTypeBuilder<GeneratorProductionReceipt> builder)
    {
        builder.ToTable("generator_production_receipts");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").HasColumnType("char(36)");
        builder.Property(value => value.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(value => value.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64).UseCollation("ascii_bin");
        builder.Property(value => value.GeneratorId).HasColumnName("generator_id").HasMaxLength(32).UseCollation("ascii_bin");
        builder.Property(value => value.ResponseJson).HasColumnName("response_json").HasColumnType("longtext");
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        builder.HasIndex(value => new { value.PlayerId, value.IdempotencyKey })
            .IsUnique().HasDatabaseName("ux_generator_receipts_player_idempotency");
        builder.HasOne<Player>().WithMany().HasForeignKey(value => value.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
