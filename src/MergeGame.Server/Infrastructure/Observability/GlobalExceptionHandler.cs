using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MergeGame.Server.Infrastructure.Observability;

/// <summary>
/// 처리되지 않은 예외를 내부 정보가 노출되지 않는 RFC 7807 응답으로 변환합니다.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetailsService;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IProblemDetailsService problemDetailsService)
    {
        _logger = logger;
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // 예외 객체는 서버 로그에만 남기고 응답에는 스택, SQL, 연결 문자열을 포함하지 않습니다.
        _logger.LogError(
            exception,
            "Unhandled request exception. TraceId={TraceId} Method={Method} Path={Path}",
            httpContext.TraceIdentifier,
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "서버 내부 오류가 발생했습니다.",
            Detail = "잠시 후 다시 시도하고, 문제가 지속되면 traceId를 전달해 주세요.",
            Type = "https://httpstatuses.com/500",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = "internal_server_error";
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
