using System.Text.Json;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Boards;

/// <summary>
/// 서버 판정 보드 액션과 머지 퀘스트 이벤트, 멱등 영수증을 하나의 DB 저장 작업으로 조정합니다.
/// </summary>
public sealed class ApplyBoardActionService
{
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MergeGameDbContext _dbContext;
    private readonly IItemCatalog _itemCatalog;
    private readonly TimeProvider _timeProvider;

    public ApplyBoardActionService(
        MergeGameDbContext dbContext,
        IItemCatalog itemCatalog,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _itemCatalog = itemCatalog;
        _timeProvider = timeProvider;
    }

    public async Task<ApplyBoardActionServiceResult> ExecuteAsync(
        Guid playerId,
        int sourceSlot,
        int targetSlot,
        long expectedBoardRevision,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        idempotencyKey = idempotencyKey.Trim();
        var replay = await TryReplayAsync(
            playerId, sourceSlot, targetSlot, idempotencyKey, cancellationToken);
        if (replay is not null)
            return replay;

        var board = await _dbContext.PlayerBoards.Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        if (board is null)
            return ApplyBoardActionServiceResult.Failed(BoardActionServiceError.BoardNotInitialized);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var actionResult = board.TryApplyAction(
            sourceSlot, targetSlot, expectedBoardRevision, _itemCatalog, now);
        if (!actionResult.Success)
        {
            return ApplyBoardActionServiceResult.Failed(
                MapError(actionResult.Error),
                BoardStateMapper.Map(board, _itemCatalog));
        }

        if (actionResult.Action == BoardActionType.Merged)
        {
            // 기존 머지 API와 동일하게 통합 액션의 머지도 퀘스트 및 게임 이벤트에 반영합니다.
            var quest = await _dbContext.PlayerQuests.SingleOrDefaultAsync(
                value => value.PlayerId == playerId && value.QuestId == PlayerQuest.FirstMergeQuestId,
                cancellationToken);
            quest?.RecordSuccessfulMerge(now);
            _dbContext.GameplayEvents.Add(GameplayEvent.CreateMerge(
                playerId, board.Revision, actionResult.ResultItem!.Level, now));
        }

        var boardState = BoardStateMapper.Map(board, _itemCatalog);
        var resultItem = boardState.Items.Single(value => value.ItemId == actionResult.ResultItem!.Id);
        var response = new BoardActionResponse(
            boardState,
            ToWireAction(actionResult.Action!.Value),
            sourceSlot,
            targetSlot,
            resultItem,
            Replayed: false);
        _dbContext.BoardActionReceipts.Add(BoardActionReceipt.Create(
            playerId,
            idempotencyKey,
            sourceSlot,
            targetSlot,
            JsonSerializer.Serialize(response, ReceiptJsonOptions),
            now));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ApplyBoardActionServiceResult.Succeeded(response);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            var concurrentReplay = await TryReplayAsync(
                playerId, sourceSlot, targetSlot, idempotencyKey, cancellationToken);
            return concurrentReplay
                ?? ApplyBoardActionServiceResult.Failed(BoardActionServiceError.StaleRevision);
        }
    }

    private async Task<ApplyBoardActionServiceResult?> TryReplayAsync(
        Guid playerId,
        int sourceSlot,
        int targetSlot,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var receipt = await _dbContext.BoardActionReceipts.AsNoTracking().SingleOrDefaultAsync(
            value => value.PlayerId == playerId && value.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (receipt is null)
            return null;
        if (receipt.SourceSlot != sourceSlot || receipt.TargetSlot != targetSlot)
            return ApplyBoardActionServiceResult.Failed(BoardActionServiceError.IdempotencyKeyConflict);

        var response = JsonSerializer.Deserialize<BoardActionResponse>(
            receipt.ResponseJson, ReceiptJsonOptions)
            ?? throw new InvalidOperationException("보드 액션 멱등 영수증을 복원할 수 없습니다.");
        return ApplyBoardActionServiceResult.Succeeded(response with { Replayed = true });
    }

    private static string ToWireAction(BoardActionType action) => action switch
    {
        BoardActionType.Moved => "move",
        BoardActionType.Merged => "merge",
        BoardActionType.Swapped => "swap",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static BoardActionServiceError MapError(BoardActionError error) => error switch
    {
        BoardActionError.StaleRevision => BoardActionServiceError.StaleRevision,
        BoardActionError.InvalidSlot => BoardActionServiceError.InvalidSlot,
        BoardActionError.SameSlot => BoardActionServiceError.SameSlot,
        BoardActionError.EmptySourceSlot => BoardActionServiceError.EmptySourceSlot,
        BoardActionError.UnknownItemDefinition => BoardActionServiceError.UnknownItemDefinition,
        BoardActionError.MaxLevelReached => BoardActionServiceError.MaxLevelReached,
        _ => throw new ArgumentOutOfRangeException(nameof(error))
    };
}

public enum BoardActionServiceError
{
    None,
    BoardNotInitialized,
    StaleRevision,
    InvalidSlot,
    SameSlot,
    EmptySourceSlot,
    UnknownItemDefinition,
    MaxLevelReached,
    IdempotencyKeyConflict
}

public sealed record BoardActionResponse(
    BoardState Board,
    string Action,
    int SourceSlot,
    int TargetSlot,
    BoardItemState ResultItem,
    bool Replayed);

public sealed record ApplyBoardActionServiceResult(
    bool Success,
    BoardActionServiceError Error,
    BoardActionResponse? Response,
    BoardState? Board)
{
    public static ApplyBoardActionServiceResult Succeeded(BoardActionResponse response) =>
        new(true, BoardActionServiceError.None, response, response.Board);

    public static ApplyBoardActionServiceResult Failed(
        BoardActionServiceError error,
        BoardState? board = null) => new(false, error, null, board);
}
