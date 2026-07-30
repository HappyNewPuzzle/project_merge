using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MergeGame.Server.Infrastructure.Observability;

/// <summary>
/// 요청과 응답, 구조화 로그, ProblemDetails를 연결하는 안전한 trace ID를 관리합니다.
/// </summary>
public sealed partial class RequestTraceMiddleware
{
    public const string HeaderName = "X-Trace-Id";
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTraceMiddleware> _logger;

    public RequestTraceMiddleware(
        RequestDelegate next,
        ILogger<RequestTraceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var suppliedTraceId = context.Request.Headers[HeaderName].FirstOrDefault();
        var traceId = IsValid(suppliedTraceId)
            ? suppliedTraceId!
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.TraceIdentifier = traceId;
        context.Response.Headers[HeaderName] = traceId;

        // BeginScope의 키는 JSON/클라우드 로그 수집기에서 검색 가능한 구조화 필드가 됩니다.
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = traceId
        }))
        {
            await _next(context);
        }
    }

    private static bool IsValid(string? value)
    {
        return value is { Length: >= 8 and <= 64 }
            && SafeTraceIdPattern().IsMatch(value);
    }

    // 로그 삽입과 헤더 제어문자 공격을 막기 위해 영문, 숫자, 하이픈만 허용합니다.
    [GeneratedRegex("^[A-Za-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTraceIdPattern();
}
