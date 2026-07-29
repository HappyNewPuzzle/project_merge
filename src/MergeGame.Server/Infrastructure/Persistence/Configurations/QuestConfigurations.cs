using MergeGame.Server.Domain.Players;
using MergeGame.Server.Domain.Quests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MergeGame.Server.Infrastructure.Persistence.Configurations;

/// <summary>퀘스트 진행도 테이블과 동시성 revision을 구성합니다.</summary>
public sealed class PlayerQuestConfiguration : IEntityTypeConfiguration<PlayerQuest>
{
    public void Configure(EntityTypeBuilder<PlayerQuest> builder)
    {
        builder.ToTable("player_quests");
        builder.HasKey(quest => new { quest.PlayerId, quest.QuestId });
        builder.Property(quest => quest.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(quest => quest.QuestId).HasColumnName("quest_id").HasMaxLength(64).UseCollation("ascii_bin");
        builder.Property(quest => quest.CurrentCount).HasColumnName("current_count");
        builder.Property(quest => quest.TargetCount).HasColumnName("target_count");
        builder.Property(quest => quest.RewardCoins).HasColumnName("reward_coins").HasColumnType("bigint");
        builder.Property(quest => quest.Revision).HasColumnName("revision").HasColumnType("bigint").IsConcurrencyToken();
        builder.Property(quest => quest.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("datetime(6)");
        builder.Property(quest => quest.ClaimedAtUtc).HasColumnName("claimed_at_utc").HasColumnType("datetime(6)");
        builder.HasOne<Player>().WithMany().HasForeignKey(quest => quest.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>서버 확정 게임 이벤트를 시간순 조회 가능한 테이블로 구성합니다.</summary>
public sealed class GameplayEventConfiguration : IEntityTypeConfiguration<GameplayEvent>
{
    public void Configure(EntityTypeBuilder<GameplayEvent> builder)
    {
        builder.ToTable("gameplay_events");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).HasColumnName("id").HasColumnType("char(36)");
        builder.Property(value => value.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(value => value.EventType).HasColumnName("event_type").HasMaxLength(32).UseCollation("ascii_bin");
        builder.Property(value => value.BoardRevision).HasColumnName("board_revision").HasColumnType("bigint");
        builder.Property(value => value.ResultItemLevel).HasColumnName("result_item_level");
        builder.Property(value => value.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("datetime(6)");
        builder.HasIndex(value => new { value.PlayerId, value.OccurredAtUtc }).HasDatabaseName("ix_gameplay_events_player_time");
        builder.HasOne<Player>().WithMany().HasForeignKey(value => value.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>플레이어와 멱등성 키의 복합 기본 키로 중복 보상 원장을 구성합니다.</summary>
public sealed class RewardClaimConfiguration : IEntityTypeConfiguration<RewardClaim>
{
    public void Configure(EntityTypeBuilder<RewardClaim> builder)
    {
        builder.ToTable("reward_claims");
        builder.HasKey(value => new { value.PlayerId, value.IdempotencyKey });
        builder.Property(value => value.PlayerId).HasColumnName("player_id").HasColumnType("char(36)");
        builder.Property(value => value.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64).UseCollation("ascii_bin");
        builder.Property(value => value.QuestId).HasColumnName("quest_id").HasMaxLength(64).UseCollation("ascii_bin");
        builder.Property(value => value.RewardCoins).HasColumnName("reward_coins").HasColumnType("bigint");
        builder.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)");
        builder.HasOne<Player>().WithMany().HasForeignKey(value => value.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
