using MergeGame.Server.Application.Boards;
using MergeGame.Server.Application.Inventory;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>인증 플레이어의 보관함 조회와 보드 왕복 이동 API를 등록합니다.</summary>
public static class InventoryEndpoints
{
    public static WebApplication MapInventoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/inventory").WithTags("Inventory").RequireAuthorization();
        group.MapGet("/", GetAsync).WithName("GetInventory")
            .Produces<InventoryState>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapPost("/store", StoreAsync).WithName("StoreBoardItem")
            .Produces<InventoryTransferResponse>(StatusCodes.Status200OK)
            .Produces<InventoryTransferErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<InventoryTransferErrorResponse>(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/items/{itemId:guid}/restore", RestoreAsync).WithName("RestoreInventoryItem")
            .Produces<InventoryTransferResponse>(StatusCodes.Status200OK)
            .Produces<InventoryTransferErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<InventoryTransferErrorResponse>(StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static async Task<IResult> GetAsync(
        ICurrentPlayerAccessor accessor, GetInventoryService service, CancellationToken token)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var state = await service.ExecuteAsync(playerId, token);
        return state is null ? Results.NotFound() : Results.Ok(state);
    }

    private static async Task<IResult> StoreAsync(
        StoreInventoryItemRequest request,
        ICurrentPlayerAccessor accessor,
        TransferInventoryItemService service,
        CancellationToken token)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var validation = ValidateKey(request.IdempotencyKey);
        if (validation is not null) return validation;
        return ToResult(await service.StoreAsync(
            playerId, request.ItemId, request.ExpectedBoardRevision,
            request.ExpectedInventoryRevision, request.IdempotencyKey.Trim(), token));
    }

    private static async Task<IResult> RestoreAsync(
        Guid itemId,
        RestoreInventoryItemRequest request,
        ICurrentPlayerAccessor accessor,
        TransferInventoryItemService service,
        CancellationToken token)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var validation = ValidateKey(request.IdempotencyKey);
        if (validation is not null) return validation;
        return ToResult(await service.RestoreAsync(
            playerId, itemId, request.ExpectedBoardRevision,
            request.ExpectedInventoryRevision, request.IdempotencyKey.Trim(), token));
    }

    private static IResult? ValidateKey(string? key) => string.IsNullOrWhiteSpace(key) || key.Length > 64
        ? Results.BadRequest(new InventoryTransferErrorResponse(
            "invalid_idempotency_key", "idempotencyKey는 1자 이상 64자 이하여야 합니다.", null, null))
        : null;

    private static IResult ToResult(InventoryTransferServiceResult result)
    {
        if (result.Success) return Results.Ok(result.Response);
        var response = new InventoryTransferErrorResponse(
            result.Error switch
            {
                InventoryServiceError.StaleRevision => "stale_revision",
                InventoryServiceError.ItemNotFound => "item_not_found",
                InventoryServiceError.InventoryFull => "inventory_full",
                InventoryServiceError.FullBoard => "full_board",
                InventoryServiceError.IdempotencyKeyConflict => "idempotency_key_conflict",
                _ => "not_initialized"
            },
            "보드와 인벤토리 상태를 확인한 뒤 다시 시도해 주세요.",
            result.Board,
            result.Inventory);
        return result.Error is InventoryServiceError.StaleRevision or InventoryServiceError.IdempotencyKeyConflict
            ? Results.Conflict(response)
            : Results.UnprocessableEntity(response);
    }
}

public sealed record StoreInventoryItemRequest(
    Guid ItemId,
    long ExpectedBoardRevision,
    long ExpectedInventoryRevision,
    string IdempotencyKey);
public sealed record RestoreInventoryItemRequest(
    long ExpectedBoardRevision,
    long ExpectedInventoryRevision,
    string IdempotencyKey);
public sealed record InventoryTransferErrorResponse(
    string Code,
    string Message,
    BoardState? Board,
    InventoryState? Inventory);
