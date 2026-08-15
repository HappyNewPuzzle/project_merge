using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Content;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Generators;

namespace MergeGame.Server.Application.Content;

/// <summary>서버의 실제 도메인 카탈로그를 Unity가 캐시 가능한 공개 콘텐츠 계약으로 변환합니다.</summary>
public sealed class GetContentCatalogService
{
    private readonly IItemCatalog _itemCatalog;
    private readonly IGeneratorCatalog _generatorCatalog;

    public GetContentCatalogService(IItemCatalog itemCatalog, IGeneratorCatalog generatorCatalog)
    {
        _itemCatalog = itemCatalog;
        _generatorCatalog = generatorCatalog;
    }

    public ContentCatalogResponse Execute()
    {
        var chains = _itemCatalog.GetAll()
            .GroupBy(value => value.ChainId, StringComparer.Ordinal)
            .Select(group => new ItemChainCatalogState(
                group.Key,
                group.OrderBy(value => value.Level)
                    .Select(value => new ItemCatalogState(
                        value.Level, value.Name, value.IsMaxLevel))
                    .ToArray()))
            .OrderBy(value => value.ChainId, StringComparer.Ordinal)
            .ToArray();
        var generators = _generatorCatalog.GetAll()
            .Select(value => new GeneratorCatalogState(
                value.Id,
                value.GeneratedChainId,
                value.GeneratedLevel,
                value.EnergyCost,
                value.MaxCharges,
                checked((int)value.ChargeRecoveryInterval.TotalSeconds)))
            .OrderBy(value => value.GeneratorId, StringComparer.Ordinal)
            .ToArray();

        return new ContentCatalogResponse(
            GameContentVersion.Current,
            new BoardRulesState(PlayerBoard.Width, PlayerBoard.Height, PlayerBoard.SlotCount),
            new EconomyRulesState(
                PlayerEconomy.MaxEnergy,
                checked((int)PlayerEconomy.EnergyRechargeInterval.TotalSeconds),
                PlayerEconomy.DailyCoinReward,
                PlayerEconomy.FriendEnergyGiftAmount),
            chains,
            generators);
    }
}

public sealed record ContentCatalogResponse(
    string Version,
    BoardRulesState Board,
    EconomyRulesState Economy,
    IReadOnlyList<ItemChainCatalogState> ItemChains,
    IReadOnlyList<GeneratorCatalogState> Generators);

public sealed record BoardRulesState(int Width, int Height, int SlotCount);
public sealed record EconomyRulesState(
    int MaxEnergy,
    int EnergyRechargeSeconds,
    long DailyCoinReward,
    int FriendEnergyGiftAmount);
public sealed record ItemChainCatalogState(string ChainId, IReadOnlyList<ItemCatalogState> Levels);
public sealed record ItemCatalogState(int Level, string Name, bool IsMaxLevel);
public sealed record GeneratorCatalogState(
    string GeneratorId,
    string GeneratedChainId,
    int GeneratedLevel,
    int EnergyCost,
    int MaxCharges,
    int ChargeRecoverySeconds);
