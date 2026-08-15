using MergeGame.Server.Application.Economy;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>
/// 인증 플레이어의 에너지, 코인, 생성기, 일일 보상 API를 등록합니다.
/// </summary>
public static class EconomyEndpoints
{
    public static WebApplication MapEconomyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/economy")
            .WithTags("Economy")
            .RequireAuthorization();
        group.MapPost("/", InitializeAsync).WithName("InitializeEconomy")
            .Produces<EconomySnapshot>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/", GetAsync).WithName("GetEconomy")
            .Produces<EconomySnapshot>(StatusCodes.Status200OK)
            .Produces<EconomyErrorResponse>(StatusCodes.Status404NotFound);
        group.MapGet("/ledger", GetLedgerAsync).WithName("GetEconomyLedger")
            .Produces<IReadOnlyList<EconomyLedgerEntryState>>(StatusCodes.Status200OK);
        group.MapPost("/generate", GenerateAsync).WithName("GenerateBoardItem")
            .Produces<GenerateItemResponse>(StatusCodes.Status200OK)
            .Produces<EconomyErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<EconomyErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<EconomyErrorResponse>(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/daily-reward", ClaimDailyRewardAsync).WithName("ClaimDailyReward")
            .Produces<EconomySnapshot>(StatusCodes.Status200OK)
            .Produces<EconomyErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<EconomyErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<EconomyErrorResponse>(StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    /// <summary>최근 에너지·코인 변경 원장을 최신 순으로 최대 100건 반환합니다.</summary>
    private static async Task<IResult> GetLedgerAsync(
        int? limit,
        ICurrentPlayerAccessor currentPlayer,
        GetEconomyLedgerService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
            return Results.Unauthorized();
        return Results.Ok(await service.ExecuteAsync(playerId, limit ?? 50, cancellationToken));
    }

    private static async Task<IResult> InitializeAsync(
        ICurrentPlayerAccessor currentPlayer,
        InitializeEconomyService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var economy = await service.ExecuteAsync(playerId, cancellationToken);
        return economy is null ? Results.NotFound() : Results.Ok(economy);
    }

    private static async Task<IResult> GetAsync(
        ICurrentPlayerAccessor currentPlayer,
        GetEconomyService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var economy = await service.ExecuteAsync(playerId, cancellationToken);
        return economy is null
            ? Results.NotFound(new EconomyErrorResponse("economy_not_initialized", "먼저 경제 상태를 초기화해야 합니다.", null))
            : Results.Ok(economy);
    }

    private static async Task<IResult> GenerateAsync(
        GenerateItemRequest request,
        ICurrentPlayerAccessor currentPlayer,
        GenerateBoardItemService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.ExecuteAsync(
            playerId,
            request.TargetSlot,
            request.ExpectedBoardRevision,
            request.ExpectedEconomyRevision,
            cancellationToken);
        if (result.Status == EconomyServiceStatus.Succeeded)
        {
            return Results.Ok(new GenerateItemResponse(result.Board!, result.Economy!));
        }

        var response = new EconomyErrorResponse(
            ToCode(result.EconomyError, result.BoardError),
            ToMessage(result.EconomyError, result.BoardError),
            result.Economy);
        return result.Status switch
        {
            EconomyServiceStatus.NotInitialized => Results.NotFound(response),
            EconomyServiceStatus.Conflict => Results.Conflict(response),
            _ => Results.UnprocessableEntity(response)
        };
    }

    private static async Task<IResult> ClaimDailyRewardAsync(
        RevisionRequest request,
        ICurrentPlayerAccessor currentPlayer,
        ClaimDailyRewardService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.ExecuteAsync(playerId, request.ExpectedRevision, cancellationToken);
        if (result.Status == EconomyServiceStatus.Succeeded)
        {
            return Results.Ok(result.Economy);
        }

        var response = new EconomyErrorResponse(
            ToCode(result.Error, Domain.Boards.BoardGenerationError.None),
            ToMessage(result.Error, Domain.Boards.BoardGenerationError.None),
            result.Economy);
        return result.Status == EconomyServiceStatus.Conflict
            ? Results.Conflict(response)
            : result.Status == EconomyServiceStatus.NotInitialized
                ? Results.NotFound(response)
                : Results.UnprocessableEntity(response);
    }

    private static string ToCode(EconomyActionError economy, Domain.Boards.BoardGenerationError board) =>
        economy switch
        {
            EconomyActionError.StaleRevision => "stale_economy_revision",
            EconomyActionError.InsufficientEnergy => "insufficient_energy",
            EconomyActionError.DailyRewardAlreadyClaimed => "daily_reward_already_claimed",
            _ => board switch
            {
                Domain.Boards.BoardGenerationError.StaleRevision => "stale_board_revision",
                Domain.Boards.BoardGenerationError.InvalidSlot => "invalid_slot",
                Domain.Boards.BoardGenerationError.SlotOccupied => "slot_occupied",
                _ => "not_initialized"
            }
        };

    private static string ToMessage(EconomyActionError economy, Domain.Boards.BoardGenerationError board) =>
        economy switch
        {
            EconomyActionError.StaleRevision => "경제 상태가 다른 요청에 의해 변경됐습니다.",
            EconomyActionError.InsufficientEnergy => "아이템 생성에 필요한 에너지가 부족합니다.",
            EconomyActionError.DailyRewardAlreadyClaimed => "오늘의 보상을 이미 받았습니다.",
            _ => board switch
            {
                Domain.Boards.BoardGenerationError.StaleRevision => "보드가 다른 요청에 의해 변경됐습니다.",
                Domain.Boards.BoardGenerationError.InvalidSlot => "슬롯은 0부터 34 사이여야 합니다.",
                Domain.Boards.BoardGenerationError.SlotOccupied => "대상 슬롯에 이미 아이템이 있습니다.",
                _ => "보드 또는 경제 상태를 먼저 초기화해야 합니다."
            }
        };
}

public sealed record GenerateItemRequest(int TargetSlot, long ExpectedBoardRevision, long ExpectedEconomyRevision);
public sealed record RevisionRequest(long ExpectedRevision);
public sealed record GenerateItemResponse(Application.Boards.BoardState Board, EconomySnapshot Economy);
public sealed record EconomyErrorResponse(string Code, string Message, EconomySnapshot? Economy);
