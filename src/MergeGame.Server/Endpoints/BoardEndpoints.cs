using MergeGame.Server.Application.Boards;
using MergeGame.Server.Domain.Boards;
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

        return app;
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

/// <summary>
/// 보드 API 실패 시 안정적인 코드와 최신 동기화 정보를 반환합니다.
/// </summary>
public sealed record BoardErrorResponse(
    string Code,
    string Message,
    long? CurrentRevision,
    BoardState? Board);
