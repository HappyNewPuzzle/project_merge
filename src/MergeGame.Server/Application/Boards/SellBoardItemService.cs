using System.Text.Json;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MergeGame.Server.Application.Quests;

namespace MergeGame.Server.Application.Boards;

/// <summary>보드 아이템 제거, 서버 가격 코인 지급과 멱등 영수증을 한 트랜잭션으로 저장합니다.</summary>
public sealed class SellBoardItemService
{
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MergeGameDbContext _dbContext;
    private readonly IItemCatalog _itemCatalog;
    private readonly TimeProvider _timeProvider;
    private readonly QuestProgressService _questProgress;

    public SellBoardItemService(
        MergeGameDbContext dbContext,
        IItemCatalog itemCatalog,
        TimeProvider timeProvider,
        QuestProgressService questProgress)
    {
        _dbContext = dbContext;
        _itemCatalog = itemCatalog;
        _timeProvider = timeProvider;
        _questProgress = questProgress;
    }

    public async Task<SellBoardItemServiceResult> ExecuteAsync(
        Guid playerId,
        Guid itemId,
        long expectedBoardRevision,
        long expectedEconomyRevision,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        idempotencyKey = idempotencyKey.Trim();
        var replay = await TryReplayAsync(playerId, itemId, idempotencyKey, cancellationToken);
        if (replay is not null)
            return replay;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var board = await _dbContext.PlayerBoards.Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        var economy = await _dbContext.PlayerEconomies.SingleOrDefaultAsync(
            value => value.PlayerId == playerId, cancellationToken);
        if (board is null || economy is null)
            return SellBoardItemServiceResult.Failed(BoardItemSaleServiceError.NotInitialized);

        var currentBoard = BoardStateMapper.Map(board, _itemCatalog);
        var currentEconomy = economy.CreateSnapshot(now);
        if (board.Revision != expectedBoardRevision || economy.Revision != expectedEconomyRevision)
        {
            return SellBoardItemServiceResult.Failed(
                BoardItemSaleServiceError.StaleRevision, currentBoard, currentEconomy);
        }

        var soldItemState = currentBoard.Items.SingleOrDefault(value => value.ItemId == itemId);
        var sale = board.TrySellItem(itemId, expectedBoardRevision, _itemCatalog, now);
        if (!sale.Success)
        {
            return SellBoardItemServiceResult.Failed(
                MapError(sale.Error), currentBoard, currentEconomy);
        }

        var economyError = economy.TryCreditCoins(expectedEconomyRevision, sale.SalePrice);
        if (economyError != EconomyActionError.None)
            throw new InvalidOperationException("판매 사전 revision 검증과 경제 변경 결과가 일치하지 않습니다.");

        var response = new SellBoardItemResponse(
            BoardStateMapper.Map(board, _itemCatalog),
            economy.CreateSnapshot(now),
            soldItemState!,
            sale.SalePrice,
            Replayed: false);
        _dbContext.BoardItemSaleReceipts.Add(BoardItemSaleReceipt.Create(
            playerId,
            idempotencyKey,
            itemId,
            JsonSerializer.Serialize(response, ReceiptJsonOptions),
            now));
        _dbContext.EconomyLedgerEntries.Add(EconomyLedgerEntry.CreateCoins(
            playerId,
            "board_item.sold",
            sale.SalePrice,
            economy.Coins,
            economy.Revision,
            $"board-item:{itemId:N}",
            now));
        await _questProgress.RecordAsync(playerId, "item_sold", now, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SellBoardItemServiceResult.Succeeded(response);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            var concurrentReplay = await TryReplayAsync(playerId, itemId, idempotencyKey, cancellationToken);
            return concurrentReplay ?? SellBoardItemServiceResult.Failed(BoardItemSaleServiceError.StaleRevision);
        }
    }

    private async Task<SellBoardItemServiceResult?> TryReplayAsync(
        Guid playerId,
        Guid itemId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var receipt = await _dbContext.BoardItemSaleReceipts.AsNoTracking().SingleOrDefaultAsync(
            value => value.PlayerId == playerId && value.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (receipt is null)
            return null;
        if (receipt.ItemId != itemId)
            return SellBoardItemServiceResult.Failed(BoardItemSaleServiceError.IdempotencyKeyConflict);

        var response = JsonSerializer.Deserialize<SellBoardItemResponse>(
            receipt.ResponseJson, ReceiptJsonOptions)
            ?? throw new InvalidOperationException("아이템 판매 멱등 영수증을 복원할 수 없습니다.");
        return SellBoardItemServiceResult.Succeeded(response with { Replayed = true });
    }

    private static BoardItemSaleServiceError MapError(BoardSaleError error) => error switch
    {
        BoardSaleError.StaleRevision => BoardItemSaleServiceError.StaleRevision,
        BoardSaleError.ItemNotFound => BoardItemSaleServiceError.ItemNotFound,
        BoardSaleError.UnknownItemDefinition => BoardItemSaleServiceError.UnknownItemDefinition,
        BoardSaleError.ItemNotSellable => BoardItemSaleServiceError.ItemNotSellable,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };
}

public enum BoardItemSaleServiceError
{
    None,
    NotInitialized,
    StaleRevision,
    ItemNotFound,
    UnknownItemDefinition,
    ItemNotSellable,
    IdempotencyKeyConflict
}

public sealed record SellBoardItemResponse(
    BoardState Board,
    EconomySnapshot Economy,
    BoardItemState SoldItem,
    long SalePrice,
    bool Replayed);

public sealed record SellBoardItemServiceResult(
    bool Success,
    BoardItemSaleServiceError Error,
    SellBoardItemResponse? Response,
    BoardState? Board,
    EconomySnapshot? Economy)
{
    public static SellBoardItemServiceResult Succeeded(SellBoardItemResponse response) =>
        new(true, BoardItemSaleServiceError.None, response, response.Board, response.Economy);
    public static SellBoardItemServiceResult Failed(
        BoardItemSaleServiceError error,
        BoardState? board = null,
        EconomySnapshot? economy = null) => new(false, error, null, board, economy);
}
