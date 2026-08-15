using MergeGame.Server.Application.Boards;
using MergeGame.Server.Application.Generators;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Generators;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>
/// 인증된 플레이어의 머지 보드 초기화, 조회, 머지 엔드포인트를 등록합니다.
/// </summary>
public static class BoardEndpoints
{
    /// <summary>
    /// 버전 1 보드 API를 등록하며 그룹 전체에 JWT 인증을 요구합니다.
    /// </summary>
    public static WebApplication MapBoardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/board")
            .WithTags("Board")
            .RequireAuthorization();

        group.MapPost("/", InitializeBoardAsync)
            .WithName("InitializeBoard")
            .Produces<BoardState>(StatusCodes.Status200OK)
            .Produces<BoardState>(StatusCodes.Status201Created)
            .Produces<BoardErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/", GetBoardAsync)
            .WithName("GetBoard")
            .Produces<BoardState>(StatusCodes.Status200OK)
            .Produces<BoardErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/merge", MergeItemsAsync)
            .WithName("MergeBoardItems")
            .Produces<BoardState>(StatusCodes.Status200OK)
            .Produces<BoardErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<BoardErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<BoardErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/actions", ApplyBoardActionAsync)
            .WithName("ApplyBoardAction")
            .Produces<BoardActionResponse>(StatusCodes.Status200OK)
            .Produces<BoardErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<BoardErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<BoardErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<BoardErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/generators/{generatorId}/produce", ProduceGeneratorItemAsync)
            .WithName("ProduceGeneratorItem")
            .Produces<GeneratorProduceResponse>(StatusCodes.Status200OK)
            .Produces<GeneratorProduceErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<GeneratorProduceErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<GeneratorProduceErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<GeneratorProduceErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>드래그한 두 슬롯을 서버 상태에 따라 이동, 머지 또는 교환으로 판정합니다.</summary>
    private static async Task<IResult> ApplyBoardActionAsync(
        ApplyBoardActionRequest request,
        ICurrentPlayerAccessor currentPlayer,
        ApplyBoardActionService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
            return Results.Unauthorized();

        var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 64)
        {
            return Results.BadRequest(new BoardErrorResponse(
                "invalid_idempotency_key",
                "idempotencyKey는 1자 이상 64자 이하여야 합니다.",
                null,
                null));
        }

        var result = await service.ExecuteAsync(
            playerId,
            request.SourceSlot,
            request.TargetSlot,
            request.ExpectedBoardRevision,
            idempotencyKey,
            cancellationToken);
        if (result.Success)
            return Results.Ok(result.Response);

        var response = new BoardErrorResponse(
            ToBoardActionErrorCode(result.Error),
            ToBoardActionErrorMessage(result.Error),
            result.Board?.Revision,
            result.Board);
        return result.Error switch
        {
            BoardActionServiceError.BoardNotInitialized => Results.NotFound(response),
            BoardActionServiceError.StaleRevision or BoardActionServiceError.IdempotencyKeyConflict =>
                Results.Conflict(response),
            _ => Results.UnprocessableEntity(response)
        };
    }

    /// <summary>
    /// 서버가 생성 아이템과 빈 슬롯을 선택하고 에너지·보드·충전 상태를 원자적으로 변경합니다.
    /// 같은 멱등 키의 성공 요청은 저장된 최초 응답만 재생합니다.
    /// </summary>
    private static async Task<IResult> ProduceGeneratorItemAsync(
        string generatorId,
        ProduceGeneratorItemRequest request,
        ICurrentPlayerAccessor currentPlayer,
        ProduceGeneratorItemService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 64)
        {
            return Results.BadRequest(new GeneratorProduceErrorResponse(
                "invalid_idempotency_key",
                "idempotencyKey는 1자 이상 64자 이하여야 합니다.",
                null,
                null,
                null));
        }

        var result = await service.ExecuteAsync(
            playerId,
            generatorId,
            request.ExpectedBoardRevision,
            request.ExpectedEconomyRevision,
            idempotencyKey,
            cancellationToken);
        if (result.Success)
        {
            return Results.Ok(result.Response);
        }

        var response = new GeneratorProduceErrorResponse(
            ToGeneratorErrorCode(result.Error),
            ToGeneratorErrorMessage(result.Error),
            result.Board,
            result.Economy,
            result.Generator);
        return result.Error switch
        {
            GeneratorProduceError.UnknownGenerator or GeneratorProduceError.NotInitialized => Results.NotFound(response),
            GeneratorProduceError.StaleRevision or GeneratorProduceError.IdempotencyKeyConflict => Results.Conflict(response),
            _ => Results.UnprocessableEntity(response)
        };
    }

    /// <summary>
    /// 플레이어 보드를 최초 한 번 생성하고 이미 있으면 현재 상태를 그대로 반환합니다.
    /// </summary>
    private static async Task<IResult> InitializeBoardAsync(
        ICurrentPlayerAccessor currentPlayer,
        InitializePlayerBoardService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.ExecuteAsync(playerId, cancellationToken);
        return result.Status switch
        {
            BoardInitializationStatus.Created =>
                Results.Created("/api/v1/board/", result.Board),
            BoardInitializationStatus.AlreadyExists =>
                Results.Ok(result.Board),
            BoardInitializationStatus.PlayerNotFound =>
                Results.NotFound(new BoardErrorResponse(
                    "player_not_found",
                    "인증된 플레이어가 더 이상 존재하지 않습니다.",
                    CurrentRevision: null,
                    Board: null)),
            _ => throw new InvalidOperationException(
                $"처리하지 않은 초기화 상태입니다: {result.Status}")
        };
    }

    /// <summary>
    /// 현재 revision과 슬롯별 아이템을 포함한 전체 보드를 반환합니다.
    /// </summary>
    private static async Task<IResult> GetBoardAsync(
        ICurrentPlayerAccessor currentPlayer,
        GetPlayerBoardService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var board = await service.ExecuteAsync(playerId, cancellationToken);
        return board is null
            ? Results.NotFound(new BoardErrorResponse(
                "board_not_initialized",
                "먼저 POST /api/v1/board/로 보드를 생성해야 합니다.",
                CurrentRevision: null,
                Board: null))
            : Results.Ok(board);
    }

    /// <summary>
    /// 두 슬롯을 서버에서 검증하고 성공하면 source를 소비해 target을 다음 단계로 올립니다.
    /// </summary>
    private static async Task<IResult> MergeItemsAsync(
        MergeBoardItemsRequest request,
        ICurrentPlayerAccessor currentPlayer,
        MergeBoardItemsService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.ExecuteAsync(
            playerId,
            request.SourceSlot,
            request.TargetSlot,
            request.ExpectedRevision,
            cancellationToken);

        if (result.Status == BoardMergeServiceStatus.Succeeded)
        {
            return Results.Ok(result.Board);
        }

        if (result.Status == BoardMergeServiceStatus.BoardNotFound)
        {
            return Results.NotFound(new BoardErrorResponse(
                "board_not_initialized",
                "먼저 보드를 생성해야 합니다.",
                CurrentRevision: null,
                Board: null));
        }

        var errorCode = ToErrorCode(result.Error);
        var message = ToErrorMessage(result.Error);
        var errorResponse = new BoardErrorResponse(
            errorCode,
            message,
            result.Board?.Revision,
            result.Board);

        // 오래된 revision은 최신 보드를 함께 반환해 클라이언트가 즉시 동기화할 수 있게 합니다.
        return result.Status == BoardMergeServiceStatus.Conflict
            ? Results.Conflict(errorResponse)
            : Results.UnprocessableEntity(errorResponse);
    }

    private static string ToErrorCode(BoardMergeError error) => error switch
    {
        BoardMergeError.StaleRevision => "stale_revision",
        BoardMergeError.InvalidSlot => "invalid_slot",
        BoardMergeError.SameSlot => "same_slot",
        BoardMergeError.EmptySlot => "empty_slot",
        BoardMergeError.ItemsDoNotMatch => "items_do_not_match",
        BoardMergeError.UnknownItemDefinition => "unknown_item_definition",
        BoardMergeError.MaxLevelReached => "max_level_reached",
        _ => "invalid_merge"
    };

    private static string ToErrorMessage(BoardMergeError error) => error switch
    {
        BoardMergeError.StaleRevision => "보드가 다른 요청에 의해 변경됐습니다.",
        BoardMergeError.InvalidSlot => "슬롯은 0부터 34 사이여야 합니다.",
        BoardMergeError.SameSlot => "서로 다른 두 슬롯을 선택해야 합니다.",
        BoardMergeError.EmptySlot => "선택한 슬롯 중 하나에 아이템이 없습니다.",
        BoardMergeError.ItemsDoNotMatch => "같은 계열과 같은 레벨의 아이템만 머지할 수 있습니다.",
        BoardMergeError.UnknownItemDefinition => "서버에 등록되지 않은 아이템입니다.",
        BoardMergeError.MaxLevelReached => "최대 레벨 아이템은 더 이상 머지할 수 없습니다.",
        _ => "유효하지 않은 머지 요청입니다."
    };

    private static string ToGeneratorErrorCode(GeneratorProduceError error) => error switch
    {
        GeneratorProduceError.FullBoard => "full_board",
        GeneratorProduceError.InsufficientEnergy => "insufficient_energy",
        GeneratorProduceError.UnknownGenerator => "unknown_generator",
        GeneratorProduceError.GeneratorCooldown => "generator_cooldown",
        GeneratorProduceError.StaleRevision => "stale_revision",
        GeneratorProduceError.IdempotencyKeyConflict => "idempotency_key_conflict",
        _ => "not_initialized"
    };

    private static string ToGeneratorErrorMessage(GeneratorProduceError error) => error switch
    {
        GeneratorProduceError.FullBoard => "보드에 빈 슬롯이 없습니다.",
        GeneratorProduceError.InsufficientEnergy => "아이템 생성에 필요한 에너지가 부족합니다.",
        GeneratorProduceError.UnknownGenerator => "서버에 등록되지 않은 생성기입니다.",
        GeneratorProduceError.GeneratorCooldown => "생성기 충전량이 없어 회복을 기다려야 합니다.",
        GeneratorProduceError.StaleRevision => "보드 또는 경제 상태가 다른 요청에 의해 변경됐습니다.",
        GeneratorProduceError.IdempotencyKeyConflict => "같은 idempotencyKey가 다른 생성기 요청에 사용됐습니다.",
        _ => "보드와 경제 상태를 먼저 초기화해야 합니다."
    };

    private static string ToBoardActionErrorCode(BoardActionServiceError error) => error switch
    {
        BoardActionServiceError.StaleRevision => "stale_revision",
        BoardActionServiceError.InvalidSlot => "invalid_slot",
        BoardActionServiceError.SameSlot => "same_slot",
        BoardActionServiceError.EmptySourceSlot => "empty_source_slot",
        BoardActionServiceError.UnknownItemDefinition => "unknown_item_definition",
        BoardActionServiceError.MaxLevelReached => "max_level_reached",
        BoardActionServiceError.IdempotencyKeyConflict => "idempotency_key_conflict",
        _ => "board_not_initialized"
    };

    private static string ToBoardActionErrorMessage(BoardActionServiceError error) => error switch
    {
        BoardActionServiceError.StaleRevision => "보드가 다른 요청에 의해 변경됐습니다.",
        BoardActionServiceError.InvalidSlot => "슬롯은 0부터 34 사이여야 합니다.",
        BoardActionServiceError.SameSlot => "서로 다른 두 슬롯을 선택해야 합니다.",
        BoardActionServiceError.EmptySourceSlot => "원본 슬롯에 이동할 아이템이 없습니다.",
        BoardActionServiceError.UnknownItemDefinition => "서버에 등록되지 않은 아이템입니다.",
        BoardActionServiceError.MaxLevelReached => "같은 최대 레벨 아이템은 더 이상 머지할 수 없습니다.",
        BoardActionServiceError.IdempotencyKeyConflict => "같은 idempotencyKey가 다른 슬롯 액션에 사용됐습니다.",
        _ => "보드를 먼저 초기화해야 합니다."
    };
}

/// <summary>
/// 두 슬롯 머지 요청입니다.
/// </summary>
/// <param name="SourceSlot">성공 시 소비되어 비게 되는 원본 슬롯입니다.</param>
/// <param name="TargetSlot">성공 시 다음 레벨 아이템이 남는 대상 슬롯입니다.</param>
/// <param name="ExpectedRevision">클라이언트가 마지막으로 확인한 보드 revision입니다.</param>
public sealed record MergeBoardItemsRequest(
    int SourceSlot,
    int TargetSlot,
    long ExpectedRevision);

/// <summary>클라이언트가 드래그한 두 슬롯과 마지막으로 확인한 revision만 전달합니다.</summary>
public sealed record ApplyBoardActionRequest(
    int SourceSlot,
    int TargetSlot,
    long ExpectedBoardRevision,
    string IdempotencyKey);

/// <summary>
/// 보드 API 실패 시 안정적인 코드와 최신 동기화 정보를 반환합니다.
/// </summary>
public sealed record BoardErrorResponse(
    string Code,
    string Message,
    long? CurrentRevision,
    BoardState? Board);

/// <summary>서버 권위형 생성 요청이며 슬롯이나 아이템 정보는 받지 않습니다.</summary>
public sealed record ProduceGeneratorItemRequest(
    long ExpectedBoardRevision,
    long ExpectedEconomyRevision,
    string IdempotencyKey);

/// <summary>생성 실패 원인과 클라이언트가 다시 동기화할 수 있는 최신 상태입니다.</summary>
public sealed record GeneratorProduceErrorResponse(
    string Code,
    string Message,
    BoardState? Board,
    EconomySnapshot? Economy,
    GeneratorState? Generator);
