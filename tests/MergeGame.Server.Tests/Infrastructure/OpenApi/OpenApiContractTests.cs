using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MergeGame.Server.Tests.Infrastructure.OpenApi;

/// <summary>
/// 실제 TestServer가 만든 JSON을 검사하므로 OpenAPI 등록 누락, 경로 변경,
/// Bearer 보안 계약 삭제를 배포 전에 발견할 수 있습니다.
/// </summary>
public sealed class OpenApiContractTests : IClassFixture<MergeGameApiFactory>
{
    private readonly HttpClient _client;

    public OpenApiContractTests(MergeGameApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task OpenApiJson_ContainsEveryVersionOneGameplayPath()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var requiredPaths = new[]
        {
            "/api/v1/players/guest", "/api/v1/players/me", "/api/v1/auth/guest",
            "/api/v1/auth/refresh", "/api/v1/auth/logout",
            "/api/v1/content/catalog",
            "/api/v1/version",
            "/api/v1/game/bootstrap",
            "/api/v1/board", "/api/v1/board/merge", "/api/v1/board/actions", "/api/v1/economy",
            "/api/v1/board/generators/{generatorId}/produce",
            "/api/v1/board/items/{itemId}/sell",
            "/api/v1/economy/generate", "/api/v1/economy/daily-reward",
            "/api/v1/economy/ledger",
            "/api/v1/inventory", "/api/v1/inventory/store",
            "/api/v1/inventory/items/{itemId}/restore",
            "/api/v1/quests", "/api/v1/quests/{questId}/claim",
            "/api/v1/social/profile", "/api/v1/social/friends",
            "/api/v1/social/friends/{friendPlayerId}/energy-gift",
            "/api/v1/admin/overview", "/api/v1/admin/players/{playerId}",
            "/api/v1/admin/players/{playerId}/suspension",
            "/api/v1/admin/players/{playerId}/coins/adjust",
            "/api/v1/admin/approvals/coin-adjustments",
            "/api/v1/admin/approvals/{approvalId}/approve"
        };

        foreach (var path in requiredPaths)
            Assert.True(paths.TryGetProperty(path, out _), $"OpenAPI에 {path} 경로가 없습니다.");

        // 이전 클라이언트용 보드 초기화는 노출하되 신규 클라이언트는 Bootstrap을 사용합니다.
        Assert.True(paths.GetProperty("/api/v1/board").GetProperty("post")
            .GetProperty("deprecated").GetBoolean());
        Assert.False(paths.GetProperty("/api/v1/game/bootstrap").GetProperty("post")
            .TryGetProperty("deprecated", out _));

        // IResult 처리기의 성공 DTO가 빠지면 SDK 생성기가 object로 생성하므로 스키마 참조도 고정합니다.
        var boardSchemaReference = paths.GetProperty("/api/v1/board")
            .GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/BoardState", boardSchemaReference);

        var produceOperation = paths.GetProperty("/api/v1/board/generators/{generatorId}/produce")
            .GetProperty("post");
        var produceRequestReference = produceOperation.GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString();
        var produceResponseReference = produceOperation.GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/ProduceGeneratorItemRequest", produceRequestReference);
        Assert.Equal("#/components/schemas/GeneratorProduceResponse", produceResponseReference);

        // 생성 결과, 비용과 대상 슬롯은 서버 권위 값이므로 요청 DTO에 다시 추가되면 계약 테스트가 실패합니다.
        var requestProperties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ProduceGeneratorItemRequest").GetProperty("properties");
        Assert.Equal(3, requestProperties.EnumerateObject().Count());
        Assert.True(requestProperties.TryGetProperty("expectedBoardRevision", out _));
        Assert.True(requestProperties.TryGetProperty("expectedEconomyRevision", out _));
        Assert.True(requestProperties.TryGetProperty("idempotencyKey", out _));
        foreach (var forbidden in new[] { "itemId", "chainId", "level", "energyCost", "cost", "targetSlot" })
            Assert.False(requestProperties.TryGetProperty(forbidden, out _), $"요청에 서버 권위 필드 {forbidden}이 노출됐습니다.");
    }

    [Fact]
    public async Task OpenApiJson_MatchesVersionControlledSnapshot()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../docs/contracts/openapi-v1.json"));
        // 의도적인 계약 변경 때만 갱신 플래그를 지정하고 차이를 코드 리뷰에 노출합니다.
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            await File.WriteAllTextAsync(path, actual!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(await File.ReadAllTextAsync(path)), actual),
            "OpenAPI 계약이 변경됐습니다. 변경을 검토한 뒤 UPDATE_OPENAPI_SNAPSHOT=1로 스냅샷을 갱신하세요.");
    }

    [Fact]
    public async Task OpenApiJson_DeclaresBearerOnlyForProtectedOperation()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        var paths = root.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/v1/players/me").GetProperty("get").TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/api/v1/social/profile").GetProperty("get").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/v1/players/guest").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/v1/content/catalog").GetProperty("get").TryGetProperty("security", out _));

        var adminScheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("AdminApiKey");
        Assert.Equal("apiKey", adminScheme.GetProperty("type").GetString());
        Assert.Equal("X-Admin-Key", adminScheme.GetProperty("name").GetString());
        var adminSecurity = paths.GetProperty("/api/v1/admin/overview").GetProperty("get")
            .GetProperty("security")[0];
        Assert.True(adminSecurity.TryGetProperty("AdminApiKey", out _));
        Assert.False(adminSecurity.TryGetProperty("Bearer", out _));
    }

    [Fact]
    public async Task ContentCatalog_UsesVersionEtagForPublicCacheRevalidation()
    {
        using var first = await _client.GetAsync("/api/v1/content/catalog");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(first.Headers.ETag);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/content/catalog");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag);
        using var cached = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
    }

    [Fact]
    public async Task AdminApi_WhenDisabledByDefault_ReturnsUnauthorizedWithoutDatabaseAccess()
    {
        using var response = await _client.GetAsync("/api/v1/admin/overview");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>외부 MySQL에 접속하지 않고 서버 파이프라인과 OpenAPI 생성기를 실행합니다.</summary>
public sealed class MergeGameApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting은 테스트 호스트를 만들기 전에 적용되어 Program.cs의 즉시 설정 검증도 통과합니다.
        builder.UseSetting("ConnectionStrings:MergeGameDatabase",
            "Server=localhost;Database=merge_game_contract;User=contract;Password=local-only;");
        builder.UseSetting("Jwt:SigningKey",
            "8f4d2c7a6b1e9f305d8c4a7e2b6f9130-contract-key");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MergeGameDatabase"] =
                    "Server=localhost;Database=merge_game_contract;User=contract;Password=local-only;",
                ["Jwt:SigningKey"] = "8f4d2c7a6b1e9f305d8c4a7e2b6f9130-contract-key"
            });
        });
    }
}
