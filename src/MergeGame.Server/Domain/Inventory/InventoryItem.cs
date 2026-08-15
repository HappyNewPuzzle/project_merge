namespace MergeGame.Server.Domain.Inventory;

/// <summary>보드 밖 보관함에 있는 아이템이며 보드 인스턴스 ID와 머지 속성을 그대로 유지합니다.</summary>
public sealed class InventoryItem
{
    private InventoryItem() { }
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string ChainId { get; private set; } = string.Empty;
    public int Level { get; private set; }

    internal static InventoryItem Create(
        Guid id, Guid playerId, string chainId, int level) => new()
        { Id = id, PlayerId = playerId, ChainId = chainId, Level = level };
}
