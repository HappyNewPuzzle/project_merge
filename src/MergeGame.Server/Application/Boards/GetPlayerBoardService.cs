using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Boards;

/// <summary>
/// 인증된 플레이어의 현재 보드와 아이템을 읽기 전용으로 조회합니다.
/// </summary>
public sealed class GetPlayerBoardService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IItemCatalog _itemCatalog;

    public GetPlayerBoardService(
        MergeGameDbContext dbContext,
        IItemCatalog itemCatalog)
    {
        _dbContext = dbContext;
        _itemCatalog = itemCatalog;
    }

    public async Task<BoardState?> ExecuteAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var board = await _dbContext.PlayerBoards
            .AsNoTracking()
            .Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(
                candidate => candidate.PlayerId == playerId,
                cancellationToken);

        return board is null ? null : BoardStateMapper.Map(board, _itemCatalog);
    }
}
