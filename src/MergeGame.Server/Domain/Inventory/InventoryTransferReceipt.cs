namespace MergeGame.Server.Domain.Inventory;

/// <summary>보드와 인벤토리 사이 성공한 이동 응답을 보존하는 멱등 영수증입니다.</summary>
public sealed class InventoryTransferReceipt
{
    private InventoryTransferReceipt() { }
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public Guid ItemId { get; private set; }
    public string ResponseJson { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static InventoryTransferReceipt Create(
        Guid playerId, string key, string action, Guid itemId, string responseJson, DateTime nowUtc) => new()
        {
            Id = Guid.NewGuid(), PlayerId = playerId, IdempotencyKey = key, Action = action,
            ItemId = itemId, ResponseJson = responseJson,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
        };
}
