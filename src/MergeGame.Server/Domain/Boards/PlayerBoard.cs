namespace MergeGame.Server.Domain.Boards;

/// <summary>
/// 한 플레이어가 소유한 5×7 머지 보드와 모든 아이템을 관리하는 애그리게이트 루트입니다.
/// 머지 규칙과 revision 변경을 한곳에서 처리해 부분 업데이트를 방지합니다.
/// </summary>
public sealed class PlayerBoard
{
    /// <summary>
    /// 보드의 가로 슬롯 수입니다.
    /// </summary>
    public const int Width = 5;

    /// <summary>
    /// 보드의 세로 슬롯 수입니다.
    /// </summary>
    public const int Height = 7;

    /// <summary>
    /// 유효한 슬롯 인덱스 수이며 0부터 34까지 사용합니다.
    /// </summary>
    public const int SlotCount = Width * Height;

    private readonly List<BoardItem> _items = [];

    // EF Core가 DB 데이터로 애그리게이트를 복원할 때 사용합니다.
    private PlayerBoard()
    {
    }

    /// <summary>
    /// 보드 소유 플레이어의 ID이며 동시에 보드의 기본 키입니다.
    /// </summary>
    public Guid PlayerId { get; private set; }

    /// <summary>
    /// 클라이언트와 서버가 같은 보드 상태를 보고 있는지 확인하는 증가 전용 버전입니다.
    /// EF Core 동시성 토큰으로도 사용되어 동시에 도착한 두 변경 중 하나만 성공합니다.
    /// </summary>
    public long Revision { get; private set; }

    /// <summary>
    /// 보드가 처음 생성된 UTC 시각입니다.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// 마지막으로 보드 상태가 바뀐 UTC 시각입니다.
    /// </summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// 외부 코드가 컬렉션을 직접 변경하지 못하도록 읽기 전용으로 노출합니다.
    /// </summary>
    public IReadOnlyCollection<BoardItem> Items => _items.AsReadOnly();

    /// <summary>
    /// 두 개의 1레벨 정원 아이템이 포함된 새 플레이어 보드를 만듭니다.
    /// </summary>
    public static PlayerBoard CreateInitial(Guid playerId, DateTime createdAtUtc)
    {
        var utcTime = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        var board = new PlayerBoard
        {
            PlayerId = playerId,
            Revision = 1,
            CreatedAtUtc = utcTime,
            UpdatedAtUtc = utcTime
        };

        // 첫 진입 직후 머지 동작을 학습할 수 있도록 같은 아이템을 인접 슬롯에 배치합니다.
        board._items.Add(BoardItem.Create(playerId, 0, "garden", 1));
        board._items.Add(BoardItem.Create(playerId, 1, "garden", 1));
        return board;
    }

    /// <summary>
    /// 두 슬롯의 아이템을 서버 규칙에 따라 머지합니다.
    /// </summary>
    public BoardMergeResult TryMerge(
        int sourceSlot,
        int targetSlot,
        long expectedRevision,
        IItemCatalog itemCatalog,
        DateTime updatedAtUtc)
    {
        if (expectedRevision != Revision)
        {
            return BoardMergeResult.Failed(BoardMergeError.StaleRevision);
        }

        if (!IsValidSlot(sourceSlot) || !IsValidSlot(targetSlot))
        {
            return BoardMergeResult.Failed(BoardMergeError.InvalidSlot);
        }

        if (sourceSlot == targetSlot)
        {
            return BoardMergeResult.Failed(BoardMergeError.SameSlot);
        }

        var sourceItem = _items.SingleOrDefault(item => item.SlotIndex == sourceSlot);
        var targetItem = _items.SingleOrDefault(item => item.SlotIndex == targetSlot);
        if (sourceItem is null || targetItem is null)
        {
            return BoardMergeResult.Failed(BoardMergeError.EmptySlot);
        }

        if (!string.Equals(
                sourceItem.ChainId,
                targetItem.ChainId,
                StringComparison.Ordinal)
            || sourceItem.Level != targetItem.Level)
        {
            return BoardMergeResult.Failed(BoardMergeError.ItemsDoNotMatch);
        }

        if (!itemCatalog.TryGet(
                targetItem.ChainId,
                targetItem.Level,
                out var currentDefinition))
        {
            return BoardMergeResult.Failed(BoardMergeError.UnknownItemDefinition);
        }

        if (currentDefinition.IsMaxLevel
            || !itemCatalog.TryGetNext(
                targetItem.ChainId,
                targetItem.Level,
                out var nextDefinition))
        {
            return BoardMergeResult.Failed(BoardMergeError.MaxLevelReached);
        }

        // 모든 검증이 끝난 뒤에만 상태를 바꿔 실패한 요청이 보드에 흔적을 남기지 않게 합니다.
        _items.Remove(sourceItem);
        targetItem.UpgradeTo(nextDefinition.Level);
        Revision++;
        UpdatedAtUtc = DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc);

        return BoardMergeResult.Succeeded(targetItem);
    }

    /// <summary>
    /// 서버 아이템 생성기가 지정한 빈 슬롯에 카탈로그로 검증된 아이템을 추가합니다.
    /// </summary>
    public BoardGenerationResult TryAddGeneratedItem(
        int slotIndex,
        long expectedRevision,
        string chainId,
        int level,
        IItemCatalog itemCatalog,
        DateTime updatedAtUtc)
    {
        if (expectedRevision != Revision)
        {
            return BoardGenerationResult.Failed(BoardGenerationError.StaleRevision);
        }

        if (!IsValidSlot(slotIndex))
        {
            return BoardGenerationResult.Failed(BoardGenerationError.InvalidSlot);
        }

        if (_items.Any(item => item.SlotIndex == slotIndex))
        {
            return BoardGenerationResult.Failed(BoardGenerationError.SlotOccupied);
        }

        if (!itemCatalog.TryGet(chainId, level, out _))
        {
            return BoardGenerationResult.Failed(
                BoardGenerationError.UnknownItemDefinition);
        }

        var item = BoardItem.Create(PlayerId, slotIndex, chainId, level);
        _items.Add(item);
        Revision++;
        UpdatedAtUtc = DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc);
        return BoardGenerationResult.Succeeded(item);
    }

    /// <summary>
    /// 한 번의 드래그 요청을 현재 보드 상태에 따라 이동, 머지 또는 교환으로 판정해 적용합니다.
    /// 클라이언트가 액션 종류를 지정하지 않으므로 조작된 요청으로 머지 규칙을 우회할 수 없습니다.
    /// </summary>
    public BoardActionResult TryApplyAction(
        int sourceSlot,
        int targetSlot,
        long expectedRevision,
        IItemCatalog itemCatalog,
        DateTime updatedAtUtc)
    {
        if (expectedRevision != Revision)
            return BoardActionResult.Failed(BoardActionError.StaleRevision);
        if (!IsValidSlot(sourceSlot) || !IsValidSlot(targetSlot))
            return BoardActionResult.Failed(BoardActionError.InvalidSlot);
        if (sourceSlot == targetSlot)
            return BoardActionResult.Failed(BoardActionError.SameSlot);

        var sourceItem = _items.SingleOrDefault(item => item.SlotIndex == sourceSlot);
        if (sourceItem is null)
            return BoardActionResult.Failed(BoardActionError.EmptySourceSlot);

        var targetItem = _items.SingleOrDefault(item => item.SlotIndex == targetSlot);
        var utcNow = DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc);
        if (targetItem is null)
        {
            sourceItem.MoveTo(targetSlot);
            Revision++;
            UpdatedAtUtc = utcNow;
            return BoardActionResult.Succeeded(BoardActionType.Moved, sourceItem);
        }

        var itemsMatch = string.Equals(sourceItem.ChainId, targetItem.ChainId, StringComparison.Ordinal)
            && sourceItem.Level == targetItem.Level;
        if (itemsMatch)
        {
            if (!itemCatalog.TryGet(targetItem.ChainId, targetItem.Level, out var currentDefinition))
                return BoardActionResult.Failed(BoardActionError.UnknownItemDefinition);
            if (currentDefinition.IsMaxLevel
                || !itemCatalog.TryGetNext(targetItem.ChainId, targetItem.Level, out var nextDefinition))
                return BoardActionResult.Failed(BoardActionError.MaxLevelReached);

            _items.Remove(sourceItem);
            targetItem.UpgradeTo(nextDefinition.Level);
            Revision++;
            UpdatedAtUtc = utcNow;
            return BoardActionResult.Succeeded(BoardActionType.Merged, targetItem);
        }

        // 서로 다른 아이템은 인스턴스 ID를 유지한 채 위치만 교환합니다.
        sourceItem.MoveTo(targetSlot);
        targetItem.MoveTo(sourceSlot);
        Revision++;
        UpdatedAtUtc = utcNow;
        return BoardActionResult.Succeeded(BoardActionType.Swapped, sourceItem);
    }

    /// <summary>
    /// 아이템 인스턴스와 revision을 검증한 뒤 보드에서 제거하고 서버 카탈로그 판매가를 확정합니다.
    /// 경제 코인 지급은 애플리케이션 서비스가 같은 트랜잭션에서 수행합니다.
    /// </summary>
    public BoardSaleResult TrySellItem(
        Guid itemId,
        long expectedRevision,
        IItemCatalog itemCatalog,
        DateTime updatedAtUtc)
    {
        if (expectedRevision != Revision)
            return BoardSaleResult.Failed(BoardSaleError.StaleRevision);
        var item = _items.SingleOrDefault(value => value.Id == itemId);
        if (item is null)
            return BoardSaleResult.Failed(BoardSaleError.ItemNotFound);
        if (!itemCatalog.TryGet(item.ChainId, item.Level, out var definition))
            return BoardSaleResult.Failed(BoardSaleError.UnknownItemDefinition);
        if (definition.SellPrice <= 0)
            return BoardSaleResult.Failed(BoardSaleError.ItemNotSellable);

        _items.Remove(item);
        Revision++;
        UpdatedAtUtc = DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc);
        return BoardSaleResult.Succeeded(item, definition.SellPrice);
    }

    /// <summary>보관함 이동을 위해 아이템을 제거하되 판매나 머지 효과는 적용하지 않습니다.</summary>
    public BoardStorageResult TryTakeForInventory(
        Guid itemId,
        long expectedRevision,
        DateTime updatedAtUtc)
    {
        if (Revision != expectedRevision)
            return BoardStorageResult.Failed(BoardStorageError.StaleRevision);
        var item = _items.SingleOrDefault(value => value.Id == itemId);
        if (item is null)
            return BoardStorageResult.Failed(BoardStorageError.ItemNotFound);
        _items.Remove(item);
        Revision++;
        UpdatedAtUtc = DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc);
        return BoardStorageResult.Succeeded(item);
    }

    /// <summary>보관함 아이템을 서버가 선택한 빈 슬롯에 같은 인스턴스 ID로 복원합니다.</summary>
    public BoardStorageResult TryRestoreFromInventory(
        Guid itemId,
        string chainId,
        int level,
        int targetSlot,
        long expectedRevision,
        IItemCatalog itemCatalog,
        DateTime updatedAtUtc)
    {
        if (Revision != expectedRevision)
            return BoardStorageResult.Failed(BoardStorageError.StaleRevision);
        if (!IsValidSlot(targetSlot) || _items.Any(value => value.SlotIndex == targetSlot))
            return BoardStorageResult.Failed(BoardStorageError.TargetUnavailable);
        if (!itemCatalog.TryGet(chainId, level, out _))
            return BoardStorageResult.Failed(BoardStorageError.UnknownItemDefinition);
        var item = BoardItem.Restore(itemId, PlayerId, targetSlot, chainId, level);
        _items.Add(item);
        Revision++;
        UpdatedAtUtc = DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc);
        return BoardStorageResult.Succeeded(item);
    }

    private static bool IsValidSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < SlotCount;
    }
}

/// <summary>서버가 현재 두 슬롯의 상태를 보고 결정한 실제 보드 액션입니다.</summary>
public enum BoardActionType { Moved, Merged, Swapped }

/// <summary>통합 보드 액션이 상태를 변경하지 않고 종료되는 구체적인 원인입니다.</summary>
public enum BoardActionError
{
    None,
    StaleRevision,
    InvalidSlot,
    SameSlot,
    EmptySourceSlot,
    UnknownItemDefinition,
    MaxLevelReached
}

/// <summary>성공한 액션 종류와 애니메이션 기준 아이템을 함께 반환합니다.</summary>
public sealed record BoardActionResult(
    bool Success,
    BoardActionType? Action,
    BoardActionError Error,
    BoardItem? ResultItem)
{
    public static BoardActionResult Succeeded(BoardActionType action, BoardItem resultItem) =>
        new(true, action, BoardActionError.None, resultItem);

    public static BoardActionResult Failed(BoardActionError error) =>
        new(false, null, error, null);
}

public enum BoardSaleError
{
    None,
    StaleRevision,
    ItemNotFound,
    UnknownItemDefinition,
    ItemNotSellable
}

/// <summary>판매로 제거된 아이템과 서버가 확정한 코인 가격입니다.</summary>
public sealed record BoardSaleResult(
    bool Success,
    BoardSaleError Error,
    BoardItem? SoldItem,
    long SalePrice)
{
    public static BoardSaleResult Succeeded(BoardItem item, long salePrice) =>
        new(true, BoardSaleError.None, item, salePrice);
    public static BoardSaleResult Failed(BoardSaleError error) =>
        new(false, error, null, 0);
}

public enum BoardStorageError
{
    None,
    StaleRevision,
    ItemNotFound,
    TargetUnavailable,
    UnknownItemDefinition
}

public sealed record BoardStorageResult(
    bool Success,
    BoardStorageError Error,
    BoardItem? Item)
{
    public static BoardStorageResult Succeeded(BoardItem item) =>
        new(true, BoardStorageError.None, item);
    public static BoardStorageResult Failed(BoardStorageError error) =>
        new(false, error, null);
}

/// <summary>
/// 생성기 아이템 배치가 실패할 수 있는 서버 검증 원인입니다.
/// </summary>
public enum BoardGenerationError
{
    None,
    StaleRevision,
    InvalidSlot,
    SlotOccupied,
    UnknownItemDefinition
}

/// <summary>
/// 생성기 아이템 배치 결과입니다.
/// </summary>
public sealed record BoardGenerationResult(
    bool Success,
    BoardGenerationError Error,
    BoardItem? GeneratedItem)
{
    public static BoardGenerationResult Succeeded(BoardItem item) =>
        new(true, BoardGenerationError.None, item);

    public static BoardGenerationResult Failed(BoardGenerationError error) =>
        new(false, error, null);
}

/// <summary>
/// 보드 머지 시 서버가 구분하는 실패 원인입니다.
/// </summary>
public enum BoardMergeError
{
    None,
    StaleRevision,
    InvalidSlot,
    SameSlot,
    EmptySlot,
    ItemsDoNotMatch,
    UnknownItemDefinition,
    MaxLevelReached
}

/// <summary>
/// 보드 머지의 성공 아이템 또는 실패 원인을 담습니다.
/// </summary>
/// <param name="Success">머지가 적용됐는지 나타냅니다.</param>
/// <param name="Error">실패 시 구체적인 서버 검증 원인입니다.</param>
/// <param name="MergedItem">성공 시 레벨이 오른 대상 아이템입니다.</param>
public sealed record BoardMergeResult(
    bool Success,
    BoardMergeError Error,
    BoardItem? MergedItem)
{
    public static BoardMergeResult Succeeded(BoardItem item) =>
        new(true, BoardMergeError.None, item);

    public static BoardMergeResult Failed(BoardMergeError error) =>
        new(false, error, null);
}
