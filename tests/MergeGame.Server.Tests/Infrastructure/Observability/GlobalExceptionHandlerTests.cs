using MergeGame.Server.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace MergeGame.Server.Tests.Infrastructure.Observability;

/// <summary>처리되지 않은 예외가 안전한 ProblemDetails로 변환되는지 검증합니다.</summary>
public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_HidesExceptionAndIncludesTraceId()
    {
        var writer = new CapturingProblemDetailsService();
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            writer);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "server-trace-1234"
        };
        context.Request.Path = "/api/v1/test";

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("secret database detail"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("server-trace-1234", writer.Problem!.Extensions["traceId"]);
        Assert.Equal("internal_server_error", writer.Problem.Extensions["code"]);
        Assert.DoesNotContain(
            "secret database detail",
            writer.Problem.Detail,
            StringComparison.Ordinal);
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetails? Problem { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Problem = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Problem = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }
}
