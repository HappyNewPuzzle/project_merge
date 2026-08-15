using MergeGame.Server.Application.Content;
using MergeGame.Server.Domain.Content;
using MergeGame.Server.Infrastructure.Generators;
using MergeGame.Server.Infrastructure.Items;

namespace MergeGame.Server.Tests.Application.Content;

/// <summary>공개 콘텐츠 응답이 실제 서버 카탈로그와 경제 상수를 정확히 반영하는지 검증합니다.</summary>
public sealed class GetContentCatalogServiceTests
{
    [Fact]
    public void Execute_ReturnsVersionedBoardItemEconomyAndGeneratorRules()
    {
        var service = new GetContentCatalogService(
            new InMemoryItemCatalog(), new InMemoryGeneratorCatalog());

        var result = service.Execute();

        Assert.Equal(GameContentVersion.Current, result.Version);
        Assert.Equal(35, result.Board.SlotCount);
        Assert.Equal(300, result.Economy.EnergyRechargeSeconds);
        var garden = Assert.Single(result.ItemChains);
        Assert.Equal(5, garden.Levels.Count);
        Assert.True(garden.Levels[^1].IsMaxLevel);
        var generator = Assert.Single(result.Generators);
        Assert.Equal(1, generator.EnergyCost);
        Assert.Equal(30, generator.ChargeRecoverySeconds);
    }
}
