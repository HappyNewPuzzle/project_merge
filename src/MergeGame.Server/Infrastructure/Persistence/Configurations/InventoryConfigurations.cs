using MergeGame.Server.Domain.Inventory;
using MergeGame.Server.Domain.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

public sealed class PlayerInventoryConfiguration : IEntityTypeConfiguration<PlayerInventory>
{
    public void Configure(EntityTypeBuilder<PlayerInventory> b)
    {
        b.ToTable("player_inventories", t => t.HasCheckConstraint("ck_player_inventories_capacity", "`capacity` > 0"));
        b.HasKey(x => x.PlayerId);
        b.Property(x => x.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        b.Property(x => x.Capacity).HasColumnName("capacity");
        b.Property(x => x.Revision).HasColumnName("revision").HasColumnType("bigint").IsConcurrencyToken();
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime(6)");
        b.HasOne<Player>().WithOne().HasForeignKey<PlayerInventory>(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> b)
    {
        b.ToTable("inventory_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("char(36)");
        b.Property(x => x.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        b.Property(x => x.ChainId).HasColumnName("chain_id").HasMaxLength(32).UseCollation("ascii_bin");
        b.Property(x => x.Level).HasColumnName("level");
    }
}

public sealed class InventoryTransferReceiptConfiguration : IEntityTypeConfiguration<InventoryTransferReceipt>
{
    public void Configure(EntityTypeBuilder<InventoryTransferReceipt> b)
    {
        b.ToTable("inventory_transfer_receipts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasColumnType("char(36)");
        b.Property(x => x.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64).UseCollation("ascii_bin");
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(16).UseCollation("ascii_bin");
        b.Property(x => x.ItemId).HasColumnName("item_id").HasColumnType("char(36)");
        b.Property(x => x.ResponseJson).HasColumnName("response_json").HasColumnType("longtext");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        b.HasIndex(x => new { x.PlayerId, x.IdempotencyKey }).IsUnique()
            .HasDatabaseName("ux_inventory_transfers_player_idempotency");
        b.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
