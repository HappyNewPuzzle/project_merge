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
}
