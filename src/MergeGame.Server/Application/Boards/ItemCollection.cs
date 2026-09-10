using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Inventory;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Boards;

/// <summary>도감은 발견한 정의 키만 제공합니다. 이름·최대 레벨 등은 기존 Content를 재사용합니다.</summary>
public sealed record CollectionEntry(string ChainId, int Level);
public sealed record CollectionState(IReadOnlyList<CollectionEntry> DiscoveredItems);

/// <summary>기존 유스케이스의 저장 직전에 호출하며 별도 SaveChanges나 트랜잭션을 만들지 않습니다.</summary>
public static class ItemCollection
{
    public static async Task<CollectionState> ReadAsync(
        MergeGameDbContext db, Guid playerId, CancellationToken token = default) => new(
        await db.ItemDiscoveries.AsNoTracking().Where(x => x.PlayerId == playerId)
            .OrderBy(x => x.ChainId).ThenBy(x => x.Level)
            .Select(x => new CollectionEntry(x.ChainId, x.Level)).ToArrayAsync(token));

    public static async Task RecordTrackedItemsAsync(MergeGameDbContext db, CancellationToken token)
    {
        // 변경 전 레벨도 수집하여 기존 계정이 첫 접속에서 바로 머지/판매해도 입력 아이템을 잃지 않습니다.
        // 실패한 게임 액션에서는 호출하지 않으며 저장은 호출자의 기존 트랜잭션이 담당합니다.
        db.ChangeTracker.DetectChanges();
        var keys = new HashSet<(Guid Player, string Chain, int Level)>();
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.Entity is BoardItem or InventoryItem))
        {
            keys.Add(((Guid)entry.Property("PlayerId").CurrentValue!,
                (string)entry.Property("ChainId").CurrentValue!, (int)entry.Property("Level").CurrentValue!));
            if (entry.State != EntityState.Added)
                keys.Add(((Guid)entry.Property("PlayerId").OriginalValue!,
                    (string)entry.Property("ChainId").OriginalValue!, (int)entry.Property("Level").OriginalValue!));
        }
        if (keys.Count == 0) return;
        var players = keys.Select(x => x.Player).Distinct().ToArray();
        var existing = await db.ItemDiscoveries.Where(x => players.Contains(x.PlayerId)).ToListAsync(token);
        keys.ExceptWith(existing.Concat(db.ItemDiscoveries.Local)
            .Select(x => (x.PlayerId, x.ChainId, x.Level)));
        foreach (var key in keys)
            db.ItemDiscoveries.Add(ItemDiscovery.Create(key.Player, key.Chain, key.Level));
    }
}
