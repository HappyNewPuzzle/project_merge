using MergeGame.Server.Application.Boards;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Economy;

/// <summary>
/// 플레이어 경제 상태를 최초 한 번 생성합니다.
/// </summary>
public sealed class InitializeEconomyService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public InitializeEconomyService(MergeGameDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<EconomySnapshot?> ExecuteAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var economy = await _dbContext.PlayerEconomies
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        if (economy is not null)
        {
            return economy.CreateSnapshot(now);
        }

        if (!await _dbContext.Players.AnyAsync(
                player => player.Id == playerId,
                cancellationToken))
        {
            return null;
        }

        economy = PlayerEconomy.CreateInitial(playerId, now);
        _dbContext.PlayerEconomies.Add(economy);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 동시 초기화의 기본 키 충돌은 생성된 행을 다시 읽어 멱등 성공으로 처리합니다.
            _dbContext.ChangeTracker.Clear();
            economy = await _dbContext.PlayerEconomies
                .SingleAsync(value => value.PlayerId == playerId, cancellationToken);
        }

        return economy.CreateSnapshot(now);
    }
}

/// <summary>
/// DB를 변경하지 않고 현재 서버 시각 기준 에너지와 재화를 조회합니다.
/// </summary>
public sealed class GetEconomyService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public GetEconomyService(MergeGameDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<EconomySnapshot?> ExecuteAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var economy = await _dbContext.PlayerEconomies
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        return economy?.CreateSnapshot(_timeProvider.GetUtcNow().UtcDateTime);
    }
}

/// <summary>
/// 일일 코인 보상을 UTC 날짜 기준 한 번 지급합니다.
/// </summary>
public sealed class ClaimDailyRewardService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ClaimDailyRewardService(MergeGameDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<EconomyActionResult> ExecuteAsync(
        Guid playerId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var economy = await _dbContext.PlayerEconomies
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        if (economy is null)
        {
            return new EconomyActionResult(EconomyServiceStatus.NotInitialized, null);
        }

        var error = economy.TryClaimDailyReward(expectedRevision, now);
        if (error != EconomyActionError.None)
        {
            return new EconomyActionResult(
                error == EconomyActionError.StaleRevision
                    ? EconomyServiceStatus.Conflict
                    : EconomyServiceStatus.InvalidAction,
                economy.CreateSnapshot(now),
                error);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await LoadConflictAsync(playerId, now, cancellationToken);
        }

        return new EconomyActionResult(
            EconomyServiceStatus.Succeeded,
            economy.CreateSnapshot(now));
    }

    private async Task<EconomyActionResult> LoadConflictAsync(
        Guid playerId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        var current = await _dbContext.PlayerEconomies
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        return new EconomyActionResult(
            EconomyServiceStatus.Conflict,
            current?.CreateSnapshot(now),
            EconomyActionError.StaleRevision);
    }
}

/// <summary>
/// 에너지를 소비하고 빈 보드 슬롯에 1레벨 정원 아이템을 원자적으로 생성합니다.
/// </summary>
public sealed class GenerateBoardItemService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IItemCatalog _itemCatalog;
    private readonly TimeProvider _timeProvider;

    public GenerateBoardItemService(
        MergeGameDbContext dbContext,
        IItemCatalog itemCatalog,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _itemCatalog = itemCatalog;
        _timeProvider = timeProvider;
    }

    public async Task<GenerateItemResult> ExecuteAsync(
        Guid playerId,
        int targetSlot,
        long expectedBoardRevision,
        long expectedEconomyRevision,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var board = await _dbContext.PlayerBoards
            .Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        var economy = await _dbContext.PlayerEconomies
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);

        if (board is null || economy is null)
        {
            return new GenerateItemResult(
                EconomyServiceStatus.NotInitialized,
                Board: board is null ? null : BoardStateMapper.Map(board, _itemCatalog),
                Economy: economy?.CreateSnapshot(now));
        }

        var boardResult = board.TryAddGeneratedItem(
            targetSlot,
            expectedBoardRevision,
            "garden",
            level: 1,
            _itemCatalog,
            now);
        if (!boardResult.Success)
        {
            return new GenerateItemResult(
                boardResult.Error == BoardGenerationError.StaleRevision
                    ? EconomyServiceStatus.Conflict
                    : EconomyServiceStatus.InvalidAction,
                BoardStateMapper.Map(board, _itemCatalog),
                economy.CreateSnapshot(now),
                BoardError: boardResult.Error);
        }

        var economyError = economy.TrySpendGeneratorEnergy(
            expectedEconomyRevision,
            now);
        if (economyError != EconomyActionError.None)
        {
            return new GenerateItemResult(
                economyError == EconomyActionError.StaleRevision
                    ? EconomyServiceStatus.Conflict
                    : EconomyServiceStatus.InvalidAction,
                Board: null,
                economy.CreateSnapshot(now),
                EconomyError: economyError);
        }

        // 새 아이템은 이미 GUID가 있어 관계 탐색만으로는 Modified로 추론될 수 있으므로 Added 상태를 명시합니다.
        _dbContext.BoardItems.Add(boardResult.GeneratedItem!);

        try
        {
            // 보드 아이템 추가, 보드 revision, 에너지와 경제 revision은 하나의 트랜잭션으로 저장됩니다.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return new GenerateItemResult(
                EconomyServiceStatus.Conflict,
                Board: null,
                Economy: null,
                EconomyError: EconomyActionError.StaleRevision,
                BoardError: BoardGenerationError.StaleRevision);
        }
        catch (DbUpdateException)
        {
            // 같은 빈 슬롯을 동시에 선택한 요청은 고유 인덱스가 최종 차단할 수 있으므로 충돌로 통일합니다.
            _dbContext.ChangeTracker.Clear();
            return new GenerateItemResult(
                EconomyServiceStatus.Conflict,
                Board: null,
                Economy: null,
                EconomyError: EconomyActionError.StaleRevision,
                BoardError: BoardGenerationError.StaleRevision);
        }

        return new GenerateItemResult(
            EconomyServiceStatus.Succeeded,
            BoardStateMapper.Map(board, _itemCatalog),
            economy.CreateSnapshot(now));
    }
}

public enum EconomyServiceStatus
{
    Succeeded,
    NotInitialized,
    InvalidAction,
    Conflict
}

public sealed record EconomyActionResult(
    EconomyServiceStatus Status,
    EconomySnapshot? Economy,
    EconomyActionError Error = EconomyActionError.None);

public sealed record GenerateItemResult(
    EconomyServiceStatus Status,
    BoardState? Board,
    EconomySnapshot? Economy,
    EconomyActionError EconomyError = EconomyActionError.None,
    BoardGenerationError BoardError = BoardGenerationError.None);
