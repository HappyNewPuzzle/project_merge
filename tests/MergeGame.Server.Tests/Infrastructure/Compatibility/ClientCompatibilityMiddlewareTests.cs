using MergeGame.Server.Infrastructure.Compatibility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MergeGame.Server.Tests.Infrastructure.Compatibility;

/// <summary>최소 버전 미만 차단과 공개 복구 경로 제외를 검증합니다.</summary>
public sealed class ClientCompatibilityMiddlewareTests
{
    [Fact]
    public async Task ProtectedApi_OldClient_ReturnsUpgradeRequiredWithoutCallingNext()
    {
        var called = false;
        var middleware = new ClientCompatibilityMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext("/api/v1/board");
        context.Request.Headers[ClientCompatibilityMiddleware.ClientVersionHeader] = "0.9.0";

        await middleware.InvokeAsync(context, new ClientCompatibilityOptions
        {
            ServerVersion = "1.2.0",
            MinimumClientVersion = "1.0.0",
            RequireVersionHeader = true
        });

        Assert.False(called);
        Assert.Equal(StatusCodes.Status426UpgradeRequired, context.Response.StatusCode);
        Assert.Equal("1.2.0", context.Response.Headers[ClientCompatibilityMiddleware.ServerVersionHeader]);
    }

    [Fact]
    public async Task PublicVersionApi_WithoutHeader_RemainsAvailable()
    {
        var called = false;
        var middleware = new ClientCompatibilityMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext("/api/v1/version");

        await middleware.InvokeAsync(context, new ClientCompatibilityOptions
        {
            MinimumClientVersion = "1.0.0",
            RequireVersionHeader = true
        });

        Assert.True(called);
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        return new DefaultHttpContext
        {
            RequestServices = services,
            Request = { Path = path },
            Response = { Body = new MemoryStream() }
        };
    }
}
