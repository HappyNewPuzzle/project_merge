using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Boards;

/// <summary>
/// 인증된 플레이어의 보드를 한 번만 생성하고 초기 아이템을 지급합니다.
/// </summary>
public sealed class InitializePlayerBoardService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IItemCatalog _itemCatalog;
    private readonly TimeProvider _timeProvider;

    public InitializePlayerBoardService(
        MergeGameDbContext dbContext,
        IItemCatalog itemCatalog,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _itemCatalog = itemCatalog;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// 기존 보드가 있으면 그대로 반환하고, 없으면 revision 1의 새 보드를 저장합니다.
    /// </summary>
    public async Task<InitializeBoardResult> ExecuteAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var existingBoard = await LoadBoardAsync(playerId, cancellationToken);
        if (existingBoard is not null)
        {
            return new InitializeBoardResult(
                BoardInitializationStatus.AlreadyExists,
                BoardStateMapper.Map(existingBoard, _itemCatalog));
        }

        // 유효한 JWT가 발급된 뒤 플레이어가 삭제된 극단적인 경우 FK 오류 대신 명확한 결과를 반환합니다.
        var playerExists = await _dbContext.Players
            .AsNoTracking()
            .AnyAsync(player => player.Id == playerId, cancellationToken);
        if (!playerExists)
        {
            return new InitializeBoardResult(
                BoardInitializationStatus.PlayerNotFound,
                Board: null);
        }

        var board = PlayerBoard.CreateInitial(
            playerId,
            _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.PlayerBoards.Add(board);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new InitializeBoardResult(
                BoardInitializationStatus.Created,
                BoardStateMapper.Map(board, _itemCatalog));
        }
        catch (DbUpdateException)
        {
            // 동일 플레이어의 초기화 요청이 동시에 도착하면 기본 키 경쟁에서 진 요청도 기존 보드를 반환합니다.
            _dbContext.ChangeTracker.Clear();
            var concurrentlyCreatedBoard = await LoadBoardAsync(
                playerId,
                cancellationToken);

            if (concurrentlyCreatedBoard is null)
            {
                throw;
            }

            return new InitializeBoardResult(
                BoardInitializationStatus.AlreadyExists,
                BoardStateMapper.Map(concurrentlyCreatedBoard, _itemCatalog));
        }
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
/// 보드 초기화 요청의 처리 결과를 구분합니다.
/// </summary>
public enum BoardInitializationStatus
{
    /// <summary>이번 요청이 새 보드를 생성했습니다.</summary>
    Created,
    /// <summary>이미 생성된 보드를 변경 없이 반환했습니다.</summary>
    AlreadyExists,
    /// <summary>JWT의 플레이어가 DB에 존재하지 않습니다.</summary>
    PlayerNotFound
}

/// <summary>
/// 초기화 상태와 성공 시 보드 전체 상태를 담습니다.
/// </summary>
public sealed record InitializeBoardResult(
    BoardInitializationStatus Status,
    BoardState? Board);
