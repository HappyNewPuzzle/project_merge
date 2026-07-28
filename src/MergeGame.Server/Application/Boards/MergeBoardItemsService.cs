using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Boards;

/// <summary>
/// 인증된 플레이어 보드의 머지를 수행하고 낙관적 동시성 충돌을 HTTP 계층용 결과로 변환합니다.
/// </summary>
public sealed class MergeBoardItemsService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IItemCatalog _itemCatalog;
    private readonly TimeProvider _timeProvider;

    public MergeBoardItemsService(
        MergeGameDbContext dbContext,
        IItemCatalog itemCatalog,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _itemCatalog = itemCatalog;
        _timeProvider = timeProvider;
    }

    public async Task<MergeBoardServiceResult> ExecuteAsync(
        Guid playerId,
        int sourceSlot,
        int targetSlot,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var board = await LoadBoardAsync(playerId, cancellationToken);
        if (board is null)
        {
            return new MergeBoardServiceResult(
                BoardMergeServiceStatus.BoardNotFound,
                BoardMergeError.None,
                Board: null);
        }

        var mergeResult = board.TryMerge(
            sourceSlot,
            targetSlot,
            expectedRevision,
            _itemCatalog,
            _timeProvider.GetUtcNow().UtcDateTime);

        if (!mergeResult.Success)
        {
            var status = mergeResult.Error == BoardMergeError.StaleRevision
                ? BoardMergeServiceStatus.Conflict
                : BoardMergeServiceStatus.InvalidMove;
            return new MergeBoardServiceResult(
                status,
                mergeResult.Error,
                BoardStateMapper.Map(board, _itemCatalog));
        }

        try
        {
            // EF Core는 보드 revision을 UPDATE WHERE 절에 포함하고 영향받은 행이 0개면 충돌 예외를 발생시킵니다.
            // 아이템 삭제와 레벨 변경은 같은 SaveChanges 트랜잭션이므로 충돌 시 모두 롤백됩니다.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var currentBoard = await LoadBoardAsync(playerId, cancellationToken);
            return new MergeBoardServiceResult(
                BoardMergeServiceStatus.Conflict,
                BoardMergeError.StaleRevision,
                currentBoard is null
                    ? null
                    : BoardStateMapper.Map(currentBoard, _itemCatalog));
        }

        return new MergeBoardServiceResult(
            BoardMergeServiceStatus.Succeeded,
            BoardMergeError.None,
            BoardStateMapper.Map(board, _itemCatalog));
    }

    private Task<PlayerBoard?> LoadBoardAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        return _dbContext.PlayerBoards
            .Include(board => board.Items)
            .SingleOrDefaultAsync(
                board => board.PlayerId == playerId,
                cancellationToken);
    }
}

/// <summary>
/// 머지 유스케이스가 HTTP 계층에 전달하는 상위 처리 상태입니다.
/// </summary>
public enum BoardMergeServiceStatus
{
    /// <summary>머지가 DB에 저장됐습니다.</summary>
    Succeeded,
    /// <summary>플레이어 보드가 아직 생성되지 않았습니다.</summary>
    BoardNotFound,
    /// <summary>서버 머지 규칙에 맞지 않는 요청입니다.</summary>
    InvalidMove,
    /// <summary>클라이언트 또는 DB 작성자의 revision이 오래됐습니다.</summary>
    Conflict
}

/// <summary>
/// 상위 상태, 세부 도메인 오류, 동기화용 보드 상태를 함께 반환합니다.
/// </summary>
public sealed record MergeBoardServiceResult(
    BoardMergeServiceStatus Status,
    BoardMergeError Error,
    BoardState? Board);
