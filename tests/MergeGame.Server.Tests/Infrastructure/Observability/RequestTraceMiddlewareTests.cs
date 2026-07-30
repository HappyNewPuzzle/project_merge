using MergeGame.Server.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace MergeGame.Server.Tests.Infrastructure.Observability;

/// <summary>요청 trace ID의 허용 형식과 응답 전달을 검증합니다.</summary>
public sealed class RequestTraceMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithSafeTraceId_ReusesItInContextAndResponse()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestTraceMiddleware.HeaderName] = "client-trace-1234";
        var middleware = new RequestTraceMiddleware(
            _ => Task.CompletedTask,
            NullLogger<RequestTraceMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal("client-trace-1234", context.TraceIdentifier);
        Assert.Equal(
            "client-trace-1234",
            context.Response.Headers[RequestTraceMiddleware.HeaderName]);
    }

    [Fact]
    public async Task InvokeAsync_WithControlCharacters_ReplacesUnsafeValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestTraceMiddleware.HeaderName] = "bad\ntrace";
        var middleware = new RequestTraceMiddleware(
            _ => Task.CompletedTask,
            NullLogger<RequestTraceMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.NotEqual("bad\ntrace", context.TraceIdentifier);
        Assert.Matches("^[a-f0-9]{32}$", context.TraceIdentifier);
    }
}
