using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;

namespace MergeGame.Server.Infrastructure.Observability;

/// <summary>
/// 상태를 변경할 수 있는 HTTP 요청의 결과를 민감정보 없이 구조화 로그로 기록합니다.
/// </summary>
public sealed class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    public AuditLoggingMiddleware(
        RequestDelegate next,
        ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var failedByException = false;
        try
        {
            await _next(context);
        }
        catch
        {
            failedByException = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var playerId = context.User
                .FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "anonymous";
            var statusCode = failedByException
                ? StatusCodes.Status500InternalServerError
                : context.Response.StatusCode;

            // Authorization 헤더, JWT, 쿼리 문자열, 요청/응답 본문은 의도적으로 기록하지 않습니다.
            _logger.LogInformation(
                "Audit request completed. TraceId={TraceId} PlayerId={PlayerId} Method={Method} Path={Path} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                context.TraceIdentifier,
                playerId,
                context.Request.Method,
                context.Request.Path.Value,
                statusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
