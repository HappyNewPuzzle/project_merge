using System.Text.Json;
using MergeGame.Server.Application.Boards;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Generators;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Generators;

/// <summary>
/// 생성기 규칙 선택, 빈 슬롯 선택, 에너지·충전량 소비, 보드 추가와 멱등 영수증 저장을 조정합니다.
/// 모든 변경을 한 번의 SaveChanges로 제출하므로 MySQL에서는 하나의 트랜잭션으로 전부 성공하거나 전부 취소됩니다.
/// </summary>
public sealed class ProduceGeneratorItemService
{
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MergeGameDbContext _dbContext;
    private readonly IItemCatalog _itemCatalog;
    private readonly IGeneratorCatalog _generatorCatalog;
    private readonly TimeProvider _timeProvider;

    public ProduceGeneratorItemService(
        MergeGameDbContext dbContext,
        IItemCatalog itemCatalog,
        IGeneratorCatalog generatorCatalog,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _itemCatalog = itemCatalog;
        _generatorCatalog = generatorCatalog;
        _timeProvider = timeProvider;
    }

    public async Task<GeneratorProduceResult> ExecuteAsync(
        Guid playerId,
        string generatorId,
        long expectedBoardRevision,
        long expectedEconomyRevision,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        generatorId = generatorId.Trim();
        idempotencyKey = idempotencyKey.Trim();

        // revision 검사보다 먼저 영수증을 확인해야 네트워크 응답을 잃은 재시도가 성공 결과를 받을 수 있습니다.
        var replay = await TryReplayAsync(playerId, generatorId, idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (!_generatorCatalog.TryGet(generatorId, out var definition))
        {
            return GeneratorProduceResult.Failed(GeneratorProduceError.UnknownGenerator);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var board = await _dbContext.PlayerBoards.Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        var economy = await _dbContext.PlayerEconomies
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        if (board is null || economy is null)
        {
            return GeneratorProduceResult.Failed(GeneratorProduceError.NotInitialized);
        }

        var generator = await _dbContext.PlayerGenerators.SingleOrDefaultAsync(
            value => value.PlayerId == playerId && value.GeneratorId == generatorId,
            cancellationToken);
        var isNewGenerator = generator is null;
        generator ??= PlayerGenerator.CreateInitial(playerId, definition, now);

        var currentBoard = BoardStateMapper.Map(board, _itemCatalog);
        var currentEconomy = economy.CreateSnapshot(now);
        var currentGenerator = generator.CreateSnapshot(now, definition);

        // 두 애그리게이트 중 하나라도 오래됐으면 어느 쪽도 변경하지 않습니다.
        if (board.Revision != expectedBoardRevision || economy.Revision != expectedEconomyRevision)
        {
            return GeneratorProduceResult.Failed(
                GeneratorProduceError.StaleRevision,
                currentBoard,
                currentEconomy,
                currentGenerator);
        }

        // 서버가 항상 가장 낮은 빈 슬롯을 선택하므로 클라이언트가 점유 슬롯을 덮어쓸 수 없습니다.
        var occupiedSlots = board.Items.Select(value => value.SlotIndex).ToHashSet();
        var targetSlot = Enumerable.Range(0, PlayerBoard.SlotCount)
            .FirstOrDefault(slot => !occupiedSlots.Contains(slot), -1);
        if (targetSlot < 0)
        {
            return GeneratorProduceResult.Failed(
                GeneratorProduceError.FullBoard,
                currentBoard,
                currentEconomy,
                currentGenerator);
        }

        // 스냅샷은 자연 회복분까지 계산하므로 실제 소비 전에 실패 조건을 모두 판정할 수 있습니다.
        if (currentEconomy.Energy < definition.EnergyCost)
        {
            return GeneratorProduceResult.Failed(
                GeneratorProduceError.InsufficientEnergy,
                currentBoard,
                currentEconomy,
                currentGenerator);
        }

        if (currentGenerator.Charges <= 0)
        {
            return GeneratorProduceResult.Failed(
                GeneratorProduceError.GeneratorCooldown,
                currentBoard,
                currentEconomy,
                currentGenerator);
        }

        // 이 아래 호출은 위에서 같은 조건을 검증했으므로 정상 경로에서는 모두 성공해야 합니다.
        var boardResult = board.TryAddGeneratedItem(
            targetSlot,
            expectedBoardRevision,
            definition.GeneratedChainId,
            definition.GeneratedLevel,
            _itemCatalog,
            now);
        var economyError = economy.TrySpendGeneratorEnergy(expectedEconomyRevision, now, definition.EnergyCost);
        var chargeConsumed = generator.TryConsumeCharge(now, definition);
        if (!boardResult.Success || economyError != EconomyActionError.None || !chargeConsumed)
        {
            throw new InvalidOperationException("생성기 사전 검증과 도메인 변경 결과가 일치하지 않습니다.");
        }

        _dbContext.BoardItems.Add(boardResult.GeneratedItem!);
        if (isNewGenerator)
        {
            _dbContext.PlayerGenerators.Add(generator);
        }

        var updatedBoard = BoardStateMapper.Map(board, _itemCatalog);
        var generatedItem = updatedBoard.Items.Single(value => value.ItemId == boardResult.GeneratedItem!.Id);
        var response = new GeneratorProduceResponse(
            updatedBoard,
            economy.CreateSnapshot(now),
            generatedItem,
            targetSlot,
            generator.CreateSnapshot(now, definition),
            Replayed: false);

        // 응답을 같은 원자적 저장에 넣어 성공한 변경인데 영수증만 없는 상태가 생기지 않게 합니다.
        _dbContext.GeneratorProductionReceipts.Add(GeneratorProductionReceipt.Create(
            playerId,
            idempotencyKey,
            generatorId,
            JsonSerializer.Serialize(response, ReceiptJsonOptions),
            now));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return GeneratorProduceResult.Succeeded(response);
        }
        catch (DbUpdateException)
        {
            // 동시 요청의 유니크 키 또는 revision 충돌이라면 추적 상태를 버리고 승자의 영수증을 재조회합니다.
            _dbContext.ChangeTracker.Clear();
            var concurrentReplay = await TryReplayAsync(
                playerId, generatorId, idempotencyKey, cancellationToken);
            return concurrentReplay ?? GeneratorProduceResult.Failed(GeneratorProduceError.StaleRevision);
        }
    }

    private async Task<GeneratorProduceResult?> TryReplayAsync(
        Guid playerId,
        string generatorId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var receipt = await _dbContext.GeneratorProductionReceipts.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.PlayerId == playerId && value.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        if (!string.Equals(receipt.GeneratorId, generatorId, StringComparison.Ordinal))
        {
            return GeneratorProduceResult.Failed(GeneratorProduceError.IdempotencyKeyConflict);
        }

        var response = JsonSerializer.Deserialize<GeneratorProduceResponse>(
            receipt.ResponseJson,
            ReceiptJsonOptions) ?? throw new InvalidOperationException("생성기 멱등 영수증을 복원할 수 없습니다.");
        return GeneratorProduceResult.Succeeded(response with { Replayed = true });
    }
}

public enum GeneratorProduceError
{
    None,
    NotInitialized,
    UnknownGenerator,
    StaleRevision,
    FullBoard,
    InsufficientEnergy,
    GeneratorCooldown,
    IdempotencyKeyConflict
}

public sealed record GeneratorProduceResponse(
    BoardState Board,
    EconomySnapshot Economy,
    BoardItemState GeneratedItem,
    int TargetSlot,
    GeneratorState Generator,
    bool Replayed);

public sealed record GeneratorProduceResult(
    bool Success,
    GeneratorProduceError Error,
    GeneratorProduceResponse? Response,
    BoardState? Board,
    EconomySnapshot? Economy,
    GeneratorState? Generator)
{
    public static GeneratorProduceResult Succeeded(GeneratorProduceResponse response) =>
        new(true, GeneratorProduceError.None, response, response.Board, response.Economy, response.Generator);

    public static GeneratorProduceResult Failed(
        GeneratorProduceError error,
        BoardState? board = null,
        EconomySnapshot? economy = null,
        GeneratorState? generator = null) => new(false, error, null, board, economy, generator);
}
