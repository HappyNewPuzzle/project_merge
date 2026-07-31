using MergeGame.Server.Domain.Players;
using MergeGame.Server.Domain.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>친구 코드의 대소문자 구분 없는 고유성과 플레이어 1:1 관계를 정의합니다.</summary>
public sealed class PlayerSocialProfileConfiguration : IEntityTypeConfiguration<PlayerSocialProfile>
{
    public void Configure(EntityTypeBuilder<PlayerSocialProfile> builder)
    {
        builder.ToTable("player_social_profiles");
        builder.HasKey(profile => profile.PlayerId);
        builder.Property(profile => profile.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(profile => profile.FriendCode).HasColumnName("friend_code").HasColumnType("char(8)")
            .UseCollation("ascii_general_ci").IsRequired();
        builder.Property(profile => profile.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        builder.HasIndex(profile => profile.FriendCode).IsUnique().HasDatabaseName("ux_social_profiles_friend_code");
        builder.HasOne<Player>().WithOne().HasForeignKey<PlayerSocialProfile>(profile => profile.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>정규화된 두 플레이어 조합이 한 번만 저장되도록 구성합니다.</summary>
public sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("friendships", table => table.HasCheckConstraint(
            "ck_friendships_distinct_players", "`player_low_id` <> `player_high_id`"));
        builder.HasKey(friendship => friendship.Id);
        builder.Property(friendship => friendship.Id).HasColumnName("id").HasColumnType("char(36)");
        builder.Property(friendship => friendship.PlayerLowId).HasColumnName("player_low_id").HasColumnType("char(36)");
        builder.Property(friendship => friendship.PlayerHighId).HasColumnName("player_high_id").HasColumnType("char(36)");
        builder.Property(friendship => friendship.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        builder.HasIndex(friendship => new { friendship.PlayerLowId, friendship.PlayerHighId })
            .IsUnique().HasDatabaseName("ux_friendships_player_pair");
        builder.HasIndex(friendship => friendship.PlayerHighId).HasDatabaseName("ix_friendships_player_high_id");
        builder.HasOne<Player>().WithMany().HasForeignKey(friendship => friendship.PlayerLowId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Player>().WithMany().HasForeignKey(friendship => friendship.PlayerHighId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>보낸 사람·받는 사람·UTC 날짜 조합의 중복 선물을 DB에서도 차단합니다.</summary>
public sealed class EnergyGiftConfiguration : IEntityTypeConfiguration<EnergyGift>
{
    public void Configure(EntityTypeBuilder<EnergyGift> builder)
    {
        builder.ToTable("energy_gifts", table => table.HasCheckConstraint(
            "ck_energy_gifts_distinct_players", "`sender_player_id` <> `recipient_player_id`"));
        builder.HasKey(gift => gift.Id);
        builder.Property(gift => gift.Id).HasColumnName("id").HasColumnType("char(36)");
        builder.Property(gift => gift.SenderPlayerId).HasColumnName("sender_player_id").HasColumnType("char(36)");
        builder.Property(gift => gift.RecipientPlayerId).HasColumnName("recipient_player_id").HasColumnType("char(36)");
        builder.Property(gift => gift.GiftDateUtc).HasColumnName("gift_date_utc").HasColumnType("date");
        builder.Property(gift => gift.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        builder.HasIndex(gift => new { gift.SenderPlayerId, gift.RecipientPlayerId, gift.GiftDateUtc })
            .IsUnique().HasDatabaseName("ux_energy_gifts_daily_sender_recipient");
        builder.HasOne<Player>().WithMany().HasForeignKey(gift => gift.SenderPlayerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Player>().WithMany().HasForeignKey(gift => gift.RecipientPlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
