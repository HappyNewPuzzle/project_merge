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
            .Produces<AdminOverview>().Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);
        group.MapGet("/players/{playerId:guid}", GetPlayerAsync).WithName("GetAdminPlayerSummary")
            .Produces<AdminPlayerSummary>().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);
        group.MapPost("/players/{playerId:guid}/suspension", ChangeSuspensionAsync).WithName("ChangePlayerSuspension")
            .Produces<SuspensionChangeResponse>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status401Unauthorized);
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
}

public sealed record ChangeSuspensionRequest(bool Suspended, string Reason, string IdempotencyKey, long ExpectedRevision);
public sealed record SuspensionChangeResponse(bool Replayed, bool IsSuspended, long Revision);
