using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MergeGame.Server.Domain.Administration;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
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

/// <summary>실제 HTTP 파이프라인에서 생성 응답 계약과 정지 계정 선차단을 확인합니다.</summary>
public sealed class GeneratorEndpointsTests
{
    /// <summary>실제 인증 파이프라인에서 발견의 영속성·격리·실패·멱등성을 함께 검사합니다.</summary>
    [Fact]
    public async Task Collection_RecordsConfirmedResultsAndSurvivesConsumption()
    {
        await using var factory = new GeneratorEndpointFactory();
        var (client, _) = await CreateAuthenticatedPlayerAsync(factory, false);
        using var bootstrap = await client.PostAsync("/api/v1/game/bootstrap", null);
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        var initial = await client.GetFromJsonAsync<MergeGame.Server.Application.Boards.CollectionState>("/api/v1/collection");
        Assert.Single(initial!.DiscoveredItems);
        Assert.Equal("garden", initial.DiscoveredItems[0].ChainId);

        var request = new { expectedBoardRevision = 1, expectedEconomyRevision = 1, idempotencyKey = "collection-toy" };
        using var generated = await client.PostAsJsonAsync("/api/v1/board/generators/toy_basic/produce", request);
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        using var replay = await client.PostAsJsonAsync("/api/v1/board/generators/toy_basic/produce", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var failed = await client.PostAsJsonAsync("/api/v1/board/generators/food_basic/produce",
            new { expectedBoardRevision = 1, expectedEconomyRevision = 1, idempotencyKey = "stale-food" });
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        using var merged = await client.PostAsJsonAsync("/api/v1/board/merge",
            new { sourceSlot = 0, targetSlot = 1, expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        var collection = await client.GetFromJsonAsync<MergeGame.Server.Application.Boards.CollectionState>("/api/v1/collection");
        Assert.Equal(3, collection!.DiscoveredItems.Count);
        Assert.Contains(collection.DiscoveredItems, x => x.ChainId == "garden" && x.Level == 1);
        Assert.Contains(collection.DiscoveredItems, x => x.ChainId == "garden" && x.Level == 2);
        Assert.Contains(collection.DiscoveredItems, x => x.ChainId == "toy" && x.Level == 1);
        Assert.DoesNotContain(collection.DiscoveredItems, x => x.ChainId == "food");
        using var again = await client.PostAsync("/api/v1/game/bootstrap", null);
        var state = await again.Content.ReadFromJsonAsync<MergeGame.Server.Application.Game.GameBootstrapResponse>();
        Assert.Equal(collection.DiscoveredItems, state!.Collection.DiscoveredItems);

        var (other, _) = await CreateAuthenticatedPlayerAsync(factory, false);
        var otherCollection = await other.GetFromJsonAsync<MergeGame.Server.Application.Boards.CollectionState>("/api/v1/collection");
        Assert.Empty(otherCollection!.DiscoveredItems);
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.GetAsync("/api/v1/collection");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Collection_LegacyBoardMerge_PreservesInputAndOutputDefinitions()
    {
        await using var factory = new GeneratorEndpointFactory();
        var (client, playerId) = await CreateAuthenticatedPlayerAsync(factory, false);
        // 도감 도입 전의 보드를 모사합니다. 발견 행 없이 바로 머지해도 입력 단계가 보존되어야 합니다.
        await SeedGameplayStateAsync(factory, playerId);
        using var merged = await client.PostAsJsonAsync("/api/v1/board/actions",
            new { sourceSlot = 0, targetSlot = 1, expectedBoardRevision = 1, idempotencyKey = "legacy-merge" });
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        var collection = await client.GetFromJsonAsync<MergeGame.Server.Application.Boards.CollectionState>("/api/v1/collection");
        Assert.Equal(new[] { 1, 2 }, collection!.DiscoveredItems.Select(x => x.Level));
    }

    [Fact]
    public async Task Produce_AuthenticatedPlayer_ReturnsServerSelectedResultAndReplays()
    {
        await using var factory = new GeneratorEndpointFactory();
        var (client, playerId) = await CreateAuthenticatedPlayerAsync(factory, suspended: false);
        await SeedGameplayStateAsync(factory, playerId);
        var request = new { expectedBoardRevision = 1, expectedEconomyRevision = 1, idempotencyKey = "http-key" };

        using var first = await client.PostAsJsonAsync("/api/v1/board/generators/garden/produce", request);
        using var replay = await client.PostAsJsonAsync("/api/v1/board/generators/garden/produce", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(2, firstJson.RootElement.GetProperty("targetSlot").GetInt32());
        Assert.Equal("garden", firstJson.RootElement.GetProperty("generatedItem").GetProperty("chainId").GetString());
        Assert.False(firstJson.RootElement.GetProperty("replayed").GetBoolean());
        Assert.True(replayJson.RootElement.GetProperty("replayed").GetBoolean());
    }

    [Fact]
    public async Task Produce_SuspendedPlayer_IsForbiddenBeforeGeneratorMutation()
    {
        await using var factory = new GeneratorEndpointFactory();
        var (client, playerId) = await CreateAuthenticatedPlayerAsync(factory, suspended: true);
        await SeedGameplayStateAsync(factory, playerId);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/board/generators/garden/produce",
            new { expectedBoardRevision = 1, expectedEconomyRevision = 1, idempotencyKey = "suspended-key" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MergeGameDbContext>();
        Assert.Empty(await db.GeneratorProductionReceipts.ToListAsync());
        Assert.Equal(100, (await db.PlayerEconomies.SingleAsync()).Energy);
    }

    private static async Task<(HttpClient Client, Guid PlayerId)> CreateAuthenticatedPlayerAsync(
        GeneratorEndpointFactory factory,
        bool suspended)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MergeGameDbContext>();
        var now = DateTime.UtcNow;
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), now);
        db.Players.Add(player);
        if (suspended)
            db.PlayerModerations.Add(PlayerModeration.Create(player.Id, true, "test suspension", now));
        await db.SaveChangesAsync();

        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenIssuer>().Issue(player.Id).Token;
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, player.Id);
    }

    private static async Task SeedGameplayStateAsync(GeneratorEndpointFactory factory, Guid playerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MergeGameDbContext>();
        var now = DateTime.UtcNow;
        db.PlayerBoards.Add(PlayerBoard.CreateInitial(playerId, now));
        db.PlayerEconomies.Add(PlayerEconomy.CreateInitial(playerId, now));
        await db.SaveChangesAsync();
    }
}

/// <summary>외부 MySQL 대신 테스트별 메모리 DB를 사용하는 실제 서버 호스트입니다.</summary>
internal sealed class GeneratorEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"generator-http-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:MergeGameDatabase",
            "Server=localhost;Database=unused;User=unused;Password=unused;");
        builder.UseSetting("Jwt:SigningKey",
            "8f4d2c7a6b1e9f305d8c4a7e2b6f9130-generator-contract-key");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:MergeGameDatabase"] =
                    "Server=localhost;Database=unused;User=unused;Password=unused;",
                ["Jwt:SigningKey"] = "8f4d2c7a6b1e9f305d8c4a7e2b6f9130-generator-contract-key"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<MergeGameDbContext>>();
            services.RemoveAll<MergeGameDbContext>();
            services.AddDbContext<MergeGameDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
