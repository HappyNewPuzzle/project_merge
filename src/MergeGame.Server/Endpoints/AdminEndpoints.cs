using System.Security.Claims;
using MergeGame.Server.Application.Administration;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>별도 관리자 키와 속도 제한으로 보호되는 읽기 전용 운영 진단 API입니다.</summary>
public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Administration")
            .RequireAuthorization(AdminAuthorizationPolicy.Name)
            .RequireRateLimiting(AdminAuthorizationPolicy.RateLimitName);
        group.MapGet("/overview", GetOverviewAsync).WithName("GetAdminOverview")
            .RequireAuthorization(AdminAuthorizationPolicy.ReadName)
            .Produces<AdminOverview>().Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);
        group.MapGet("/players/{playerId:guid}", GetPlayerAsync).WithName("GetAdminPlayerSummary")
            .RequireAuthorization(AdminAuthorizationPolicy.ReadName)
            .Produces<AdminPlayerSummary>().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);
        group.MapPost("/players/{playerId:guid}/suspension", ChangeSuspensionAsync).WithName("ChangePlayerSuspension")
            .RequireAuthorization(AdminAuthorizationPolicy.ModerationName)
            .Produces<SuspensionChangeResponse>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status401Unauthorized);
        group.MapPost("/players/{playerId:guid}/coins/adjust", AdjustCoinsAsync).WithName("AdjustPlayerCoins")
            .RequireAuthorization(AdminAuthorizationPolicy.EconomyName)
            .Produces<CoinAdjustmentResponse>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/approvals/coin-adjustments", CreateCoinApprovalAsync)
            .WithName("CreateCoinAdjustmentApproval")
            .RequireAuthorization(AdminAuthorizationPolicy.EconomyName)
            .Produces<AdminApprovalState>(StatusCodes.Status200OK)
            .Produces<AdminApprovalState>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        group.MapPost("/approvals/{approvalId:guid}/approve", ApproveCoinAdjustmentAsync)
            .WithName("ApproveCoinAdjustment")
            .RequireAuthorization(AdminAuthorizationPolicy.EconomyName)
            .Produces<ApprovalExecutionResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ApprovalExecutionResult>(StatusCodes.Status409Conflict)
            .Produces<ApprovalExecutionResult>(StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context, GetAdminOverviewService service,
        ILogger<GetAdminOverviewService> logger, CancellationToken token)
    {
        var result = await service.ExecuteAsync(token);
        logger.LogInformation("Admin overview read. OperatorId={OperatorId}", context.User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPlayerAsync(Guid playerId, HttpContext context,
        GetAdminPlayerSummaryService service, ILogger<GetAdminPlayerSummaryService> logger, CancellationToken token)
    {
        var result = await service.ExecuteAsync(playerId, token);
        logger.LogInformation("Admin player summary read. OperatorId={OperatorId} TargetPlayerId={TargetPlayerId} Found={Found}",
            context.User.FindFirstValue(ClaimTypes.NameIdentifier), playerId, result is not null);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> ChangeSuspensionAsync(Guid playerId, ChangeSuspensionRequest request,
        HttpContext context, ChangePlayerSuspensionService service, CancellationToken token)
    {
        var reason = request.Reason?.Trim() ?? ""; var key = request.IdempotencyKey?.Trim() ?? "";
        if (reason.Length is < 3 or > 256 || key.Length is < 8 or > 64)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["request"] = ["reason은 3~256자, idempotencyKey는 8~64자여야 합니다."] });
        var operatorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await service.ExecuteAsync(playerId, operatorId, key, request.Suspended, reason, request.ExpectedRevision, token);
        var response = new SuspensionChangeResponse(result.Status == SuspensionChangeStatus.Replayed, result.IsSuspended, result.Revision);
        return result.Status switch
        {
            SuspensionChangeStatus.Succeeded or SuspensionChangeStatus.Replayed => Results.Ok(response),
            SuspensionChangeStatus.NotFound => Results.NotFound(),
            _ => Results.Conflict(response)
        };
    }

    private static async Task<IResult> AdjustCoinsAsync(Guid playerId, AdjustCoinsRequest request, HttpContext context,
        AdjustPlayerCoinsService service, AdminApiOptions options, CancellationToken token)
    {
        var reason = request.Reason?.Trim() ?? ""; var key = request.IdempotencyKey?.Trim() ?? "";
        if (reason.Length is < 3 or > 256 || key.Length is < 8 or > 64 || request.Amount == 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["request"] = ["amount는 0이 아니어야 하고 reason은 3~256자, idempotencyKey는 8~64자여야 합니다."] });
        if (request.Amount != long.MinValue
            && Math.Abs(request.Amount) >= options.RequireTwoPersonApprovalAtOrAbove)
        {
            return Results.UnprocessableEntity(new ApprovalRequiredResponse(
                "two_person_approval_required",
                options.RequireTwoPersonApprovalAtOrAbove));
        }
        var result = await service.ExecuteAsync(playerId,
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, key, request.Amount, reason, request.ExpectedEconomyRevision, token);
        var response = new CoinAdjustmentResponse(result.Status == CoinAdjustmentStatus.Replayed, result.Coins, result.EconomyRevision);
        return result.Status switch
        {
            CoinAdjustmentStatus.Succeeded or CoinAdjustmentStatus.Replayed => Results.Ok(response),
            CoinAdjustmentStatus.NotFound => Results.NotFound(),
            CoinAdjustmentStatus.Conflict or CoinAdjustmentStatus.IdempotencyConflict => Results.Conflict(response),
            _ => Results.UnprocessableEntity(response)
        };
    }

    private static async Task<IResult> CreateCoinApprovalAsync(
        CreateCoinApprovalRequest request,
        HttpContext context,
        CreateCoinAdjustmentApprovalService service,
        AdminApiOptions options,
        CancellationToken token)
    {
        var reason = request.Reason?.Trim() ?? "";
        var key = request.IdempotencyKey?.Trim() ?? "";
        if (request.Amount == long.MinValue
            || Math.Abs(request.Amount) < options.RequireTwoPersonApprovalAtOrAbove
            || Math.Abs(request.Amount) > options.MaxAbsoluteCoinAdjustment
            || reason.Length is < 3 or > 256
            || key.Length is < 8 or > 64)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["request"] = ["승인 기준 이상·최대 한도 이하 금액과 유효한 reason/idempotencyKey가 필요합니다."] });

        var result = await service.ExecuteAsync(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            key,
            request.PlayerId,
            request.Amount,
            reason,
            request.ExpectedEconomyRevision,
            token);
        return result is null
            ? Results.NotFound()
            : Results.Created($"/api/v1/admin/approvals/{result.ApprovalId}", result);
    }

    private static async Task<IResult> ApproveCoinAdjustmentAsync(
        Guid approvalId,
        HttpContext context,
        ApproveCoinAdjustmentService service,
        CancellationToken token)
    {
        var result = await service.ExecuteAsync(
            approvalId, context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, token);
        return result.Status switch
        {
            ApprovalExecutionStatus.Succeeded or ApprovalExecutionStatus.Replayed => Results.Ok(result),
            ApprovalExecutionStatus.NotFound => Results.NotFound(),
            ApprovalExecutionStatus.SameOperator => Results.Conflict(result),
            _ => Results.UnprocessableEntity(result)
        };
    }
}

public sealed record ChangeSuspensionRequest(bool Suspended, string Reason, string IdempotencyKey, long ExpectedRevision);
public sealed record SuspensionChangeResponse(bool Replayed, bool IsSuspended, long Revision);
public sealed record AdjustCoinsRequest(long Amount, string Reason, string IdempotencyKey, long ExpectedEconomyRevision);
public sealed record CoinAdjustmentResponse(bool Replayed, long Coins, long EconomyRevision);
public sealed record ApprovalRequiredResponse(string Code, long Threshold);
public sealed record CreateCoinApprovalRequest(
    Guid PlayerId,
    long Amount,
    string Reason,
    string IdempotencyKey,
    long ExpectedEconomyRevision);
