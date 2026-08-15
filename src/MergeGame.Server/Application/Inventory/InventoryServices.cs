using System.Text.Json;
using MergeGame.Server.Application.Boards;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Inventory;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Inventory;

public static class InventoryStateMapper
{
    public static InventoryState Map(PlayerInventory inventory, IItemCatalog catalog) => new(
        inventory.PlayerId,
        inventory.Capacity,
        inventory.Revision,
        inventory.Items.OrderBy(value => value.Id).Select(value =>
        {
            if (!catalog.TryGet(value.ChainId, value.Level, out var definition))
                throw new InvalidOperationException($"알 수 없는 인벤토리 아이템입니다: {value.ChainId}:{value.Level}");
            return new InventoryItemState(value.Id, value.ChainId, value.Level, definition.Name, definition.IsMaxLevel);
        }).ToArray());
}

public sealed record InventoryState(
    Guid PlayerId,
    int Capacity,
    long Revision,
    IReadOnlyList<InventoryItemState> Items);
public sealed record InventoryItemState(
    Guid ItemId,
    string ChainId,
    int Level,
    string Name,
    bool IsMaxLevel);

/// <summary>현재 인벤토리를 읽기 전용으로 조회합니다.</summary>
public sealed class GetInventoryService
{
    private readonly MergeGameDbContext _db;
    private readonly IItemCatalog _catalog;
    public GetInventoryService(MergeGameDbContext db, IItemCatalog catalog)
    { _db = db; _catalog = catalog; }

    public async Task<InventoryState?> ExecuteAsync(Guid playerId, CancellationToken token = default)
    {
        var inventory = await _db.PlayerInventories.AsNoTracking().Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, token);
        return inventory is null ? null : InventoryStateMapper.Map(inventory, _catalog);
    }
}

/// <summary>보드와 인벤토리 사이 아이템 이동과 멱등 영수증을 한 트랜잭션으로 저장합니다.</summary>
public sealed class TransferInventoryItemService
{
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MergeGameDbContext _db;
    private readonly IItemCatalog _catalog;
    private readonly TimeProvider _time;
    public TransferInventoryItemService(MergeGameDbContext db, IItemCatalog catalog, TimeProvider time)
    { _db = db; _catalog = catalog; _time = time; }

    public Task<InventoryTransferServiceResult> StoreAsync(
        Guid playerId, Guid itemId, long boardRevision, long inventoryRevision, string key,
        CancellationToken token = default) => ExecuteAsync(
            playerId, itemId, "store", boardRevision, inventoryRevision, key, token);

    public Task<InventoryTransferServiceResult> RestoreAsync(
        Guid playerId, Guid itemId, long boardRevision, long inventoryRevision, string key,
        CancellationToken token = default) => ExecuteAsync(
            playerId, itemId, "restore", boardRevision, inventoryRevision, key, token);

    private async Task<InventoryTransferServiceResult> ExecuteAsync(
        Guid playerId,
        Guid itemId,
        string action,
        long expectedBoardRevision,
        long expectedInventoryRevision,
        string idempotencyKey,
        CancellationToken token)
    {
        idempotencyKey = idempotencyKey.Trim();
        var replay = await TryReplayAsync(playerId, itemId, action, idempotencyKey, token);
        if (replay is not null) return replay;

        var now = _time.GetUtcNow().UtcDateTime;
        var board = await _db.PlayerBoards.Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, token);
        var inventory = await _db.PlayerInventories.Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, token);
        if (board is null || inventory is null)
            return InventoryTransferServiceResult.Failed(InventoryServiceError.NotInitialized);
        var currentBoard = BoardStateMapper.Map(board, _catalog);
        var currentInventory = InventoryStateMapper.Map(inventory, _catalog);
        if (board.Revision != expectedBoardRevision || inventory.Revision != expectedInventoryRevision)
            return InventoryTransferServiceResult.Failed(
                InventoryServiceError.StaleRevision, currentBoard, currentInventory);

        int? targetSlot = null;
        Guid transferredItemId;
        if (action == "store")
        {
            if (inventory.Items.Count >= inventory.Capacity)
                return InventoryTransferServiceResult.Failed(
                    InventoryServiceError.InventoryFull, currentBoard, currentInventory);
            var taken = board.TryTakeForInventory(itemId, expectedBoardRevision, now);
            if (!taken.Success)
                return InventoryTransferServiceResult.Failed(
                    InventoryServiceError.ItemNotFound, currentBoard, currentInventory);
            var stored = inventory.TryStore(taken.Item!, expectedInventoryRevision, now);
            if (!stored.Success) throw new InvalidOperationException("인벤토리 저장 사전 검증 결과가 일치하지 않습니다.");
            _db.BoardItems.Remove(taken.Item!);
            _db.InventoryItems.Add(stored.Item!);
            transferredItemId = stored.Item!.Id;
        }
        else
        {
            var occupied = board.Items.Select(value => value.SlotIndex).ToHashSet();
            targetSlot = Enumerable.Range(0, PlayerBoard.SlotCount)
                .FirstOrDefault(value => !occupied.Contains(value), -1);
            if (targetSlot < 0)
                return InventoryTransferServiceResult.Failed(
                    InventoryServiceError.FullBoard, currentBoard, currentInventory);
            var inventoryItem = inventory.Items.SingleOrDefault(value => value.Id == itemId);
            if (inventoryItem is null)
                return InventoryTransferServiceResult.Failed(
                    InventoryServiceError.ItemNotFound, currentBoard, currentInventory);
            var taken = inventory.TryTake(itemId, expectedInventoryRevision, now);
            var restored = board.TryRestoreFromInventory(
                taken.Item!.Id, taken.Item.ChainId, taken.Item.Level, targetSlot.Value,
                expectedBoardRevision, _catalog, now);
            if (!restored.Success) throw new InvalidOperationException("보드 복원 사전 검증 결과가 일치하지 않습니다.");
            _db.InventoryItems.Remove(taken.Item);
            _db.BoardItems.Add(restored.Item!);
            transferredItemId = restored.Item!.Id;
        }

        var response = new InventoryTransferResponse(
            action,
            BoardStateMapper.Map(board, _catalog),
            InventoryStateMapper.Map(inventory, _catalog),
            transferredItemId,
            targetSlot,
            Replayed: false);
        _db.InventoryTransferReceipts.Add(InventoryTransferReceipt.Create(
            playerId, idempotencyKey, action, itemId,
            JsonSerializer.Serialize(response, ReceiptJsonOptions), now));
        try
        {
            await _db.SaveChangesAsync(token);
            return InventoryTransferServiceResult.Succeeded(response);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return await TryReplayAsync(playerId, itemId, action, idempotencyKey, token)
                ?? InventoryTransferServiceResult.Failed(InventoryServiceError.StaleRevision);
        }
    }

    private async Task<InventoryTransferServiceResult?> TryReplayAsync(
        Guid playerId, Guid itemId, string action, string key, CancellationToken token)
    {
        var receipt = await _db.InventoryTransferReceipts.AsNoTracking().SingleOrDefaultAsync(
            value => value.PlayerId == playerId && value.IdempotencyKey == key, token);
        if (receipt is null) return null;
        if (receipt.ItemId != itemId || !string.Equals(receipt.Action, action, StringComparison.Ordinal))
            return InventoryTransferServiceResult.Failed(InventoryServiceError.IdempotencyKeyConflict);
        var response = JsonSerializer.Deserialize<InventoryTransferResponse>(receipt.ResponseJson, ReceiptJsonOptions)
            ?? throw new InvalidOperationException("인벤토리 이동 영수증을 복원할 수 없습니다.");
        return InventoryTransferServiceResult.Succeeded(response with { Replayed = true });
    }
}

public enum InventoryServiceError
{
    None,
    NotInitialized,
    StaleRevision,
    ItemNotFound,
    InventoryFull,
    FullBoard,
    IdempotencyKeyConflict
}
public sealed record InventoryTransferResponse(
    string Action,
    BoardState Board,
    InventoryState Inventory,
    Guid ItemId,
    int? TargetSlot,
    bool Replayed);
public sealed record InventoryTransferServiceResult(
    bool Success,
    InventoryServiceError Error,
    InventoryTransferResponse? Response,
    BoardState? Board,
    InventoryState? Inventory)
{
    public static InventoryTransferServiceResult Succeeded(InventoryTransferResponse response) =>
        new(true, InventoryServiceError.None, response, response.Board, response.Inventory);
    public static InventoryTransferServiceResult Failed(
        InventoryServiceError error, BoardState? board = null, InventoryState? inventory = null) =>
        new(false, error, null, board, inventory);
}
