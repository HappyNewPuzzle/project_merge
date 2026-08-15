using MergeGame.Server.Domain.Boards;

namespace MergeGame.Server.Domain.Inventory;

/// <summary>플레이어별 제한된 보관함과 동시성 revision을 관리합니다.</summary>
public sealed class PlayerInventory
{
    public const int InitialCapacity = 20;
    private readonly List<InventoryItem> _items = [];
    private PlayerInventory() { }

    public Guid PlayerId { get; private set; }
    public int Capacity { get; private set; }
    public long Revision { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<InventoryItem> Items => _items.AsReadOnly();

    public static PlayerInventory CreateInitial(Guid playerId, DateTime nowUtc) => new()
    {
        PlayerId = playerId,
        Capacity = InitialCapacity,
        Revision = 1,
        UpdatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
    };

    public InventoryTransferResult TryStore(
        BoardItem boardItem,
        long expectedRevision,
        DateTime nowUtc)
    {
        if (Revision != expectedRevision)
            return InventoryTransferResult.Failed(InventoryTransferError.StaleRevision);
        if (_items.Count >= Capacity)
            return InventoryTransferResult.Failed(InventoryTransferError.InventoryFull);
        if (_items.Any(value => value.Id == boardItem.Id))
            return InventoryTransferResult.Failed(InventoryTransferError.ItemAlreadyStored);

        var item = InventoryItem.Create(
            boardItem.Id, PlayerId, boardItem.ChainId, boardItem.Level);
        _items.Add(item);
        Revision++;
        UpdatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        return InventoryTransferResult.Succeeded(item);
    }

    public InventoryTransferResult TryTake(
        Guid itemId,
        long expectedRevision,
        DateTime nowUtc)
    {
        if (Revision != expectedRevision)
            return InventoryTransferResult.Failed(InventoryTransferError.StaleRevision);
        var item = _items.SingleOrDefault(value => value.Id == itemId);
        if (item is null)
            return InventoryTransferResult.Failed(InventoryTransferError.ItemNotFound);

        _items.Remove(item);
        Revision++;
        UpdatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        return InventoryTransferResult.Succeeded(item);
    }
}

public enum InventoryTransferError
{
    None,
    StaleRevision,
    InventoryFull,
    ItemNotFound,
    ItemAlreadyStored
}

public sealed record InventoryTransferResult(
    bool Success,
    InventoryTransferError Error,
    InventoryItem? Item)
{
    public static InventoryTransferResult Succeeded(InventoryItem item) =>
        new(true, InventoryTransferError.None, item);
    public static InventoryTransferResult Failed(InventoryTransferError error) =>
        new(false, error, null);
}
