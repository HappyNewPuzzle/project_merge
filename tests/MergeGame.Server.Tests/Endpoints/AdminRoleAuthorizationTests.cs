using System.Net;
using System.Net.Http.Json;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MergeGame.Server.Tests.Endpoints;

/// <summary>읽기 전용 운영자 키가 조회는 가능하지만 정지·경제 변경에는 접근하지 못하는지 검증합니다.</summary>
public sealed class AdminRoleAuthorizationTests
{
    private const string ReaderKey = "reader-admin-key-with-at-least-thirty-two-bytes";

    [Fact]
    public async Task ReaderCredential_CanReadButCannotSuspendPlayer()
    {
        await using var factory = new RoleAdminFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(AdminApiKeyAuthenticationHandler.HeaderName, ReaderKey);
        var playerId = Guid.NewGuid();

        using var read = await client.GetAsync($"/api/v1/admin/players/{playerId}");
        using var mutate = await client.PostAsJsonAsync($"/api/v1/admin/players/{playerId}/suspension", new
        {
            suspended = true,
            reason = "role authorization test",
            idempotencyKey = "role-test-001",
            expectedRevision = 0
        });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, mutate.StatusCode);
    }

    private sealed class RoleAdminFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            const string jwtKey = "8f4d2c7a6b1e9f305d8c4a7e2b6f9130-admin-role-contract-key";
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:MergeGameDatabase",
                "Server=localhost;Database=unused;User=unused;Password=unused;");
            builder.UseSetting("Jwt:SigningKey", jwtKey);
            builder.UseSetting("AdminApi:Enabled", "true");
            builder.UseSetting("AdminApi:Credentials:0:OperatorId", "reader-operator");
            builder.UseSetting("AdminApi:Credentials:0:ApiKey", ReaderKey);
            builder.UseSetting("AdminApi:Credentials:0:Roles:0", AdminRoles.Reader);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MergeGameDatabase"] =
                        "Server=localhost;Database=unused;User=unused;Password=unused;",
                    ["Jwt:SigningKey"] = jwtKey,
                    ["AdminApi:Enabled"] = "true",
                    ["AdminApi:Credentials:0:OperatorId"] = "reader-operator",
                    ["AdminApi:Credentials:0:ApiKey"] = ReaderKey,
                    ["AdminApi:Credentials:0:Roles:0"] = AdminRoles.Reader
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<MergeGameDbContext>>();
                services.RemoveAll<MergeGameDbContext>();
                services.AddDbContext<MergeGameDbContext>(options =>
                    options.UseInMemoryDatabase($"admin-role-{Guid.NewGuid()}"));
            });
        }
    }
}
