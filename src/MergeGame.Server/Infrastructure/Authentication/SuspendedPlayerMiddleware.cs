using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>이미 발급된 유효 JWT도 계정 정지 직후 보호 API에서 HTTP 403으로 차단합니다.</summary>
public sealed class SuspendedPlayerMiddleware
{
    private readonly RequestDelegate _next;
    public SuspendedPlayerMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICurrentPlayerAccessor accessor, MergeGameDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true && accessor.TryGetPlayerId(out var playerId)
            && await db.PlayerModerations.AsNoTracking().AnyAsync(x => x.PlayerId == playerId && x.IsSuspended,
                context.RequestAborted))
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "정지된 계정입니다.",
                detail: "고객 지원에 traceId를 전달해 주세요.", extensions: new Dictionary<string, object?>
                { ["code"] = "account_suspended", ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
            return;
        }
        await _next(context);
    }
}
