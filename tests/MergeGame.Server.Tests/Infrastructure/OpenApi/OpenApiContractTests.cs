using System.Net;
using System.Text.Json;
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
            "/api/v1/board", "/api/v1/board/merge", "/api/v1/economy",
            "/api/v1/economy/generate", "/api/v1/economy/daily-reward",
            "/api/v1/quests", "/api/v1/quests/{questId}/claim"
        };

        foreach (var path in requiredPaths)
            Assert.True(paths.TryGetProperty(path, out _), $"OpenAPI에 {path} 경로가 없습니다.");

        // IResult 처리기의 성공 DTO가 빠지면 SDK 생성기가 object로 생성하므로 스키마 참조도 고정합니다.
        var boardSchemaReference = paths.GetProperty("/api/v1/board")
            .GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/BoardState", boardSchemaReference);
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
        Assert.False(paths.GetProperty("/api/v1/players/guest").GetProperty("post").TryGetProperty("security", out _));
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
