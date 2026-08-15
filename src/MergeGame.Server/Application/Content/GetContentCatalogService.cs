using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Content;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Generators;
using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Domain.Inventory;

namespace MergeGame.Server.Application.Content;

/// <summary>서버의 실제 도메인 카탈로그를 Unity가 캐시 가능한 공개 콘텐츠 계약으로 변환합니다.</summary>
public sealed class GetContentCatalogService
{
    private readonly IItemCatalog _itemCatalog;
    private readonly IGeneratorCatalog _generatorCatalog;
    private readonly IQuestCatalog _questCatalog;

    public GetContentCatalogService(
        IItemCatalog itemCatalog,
        IGeneratorCatalog generatorCatalog,
        IQuestCatalog questCatalog)
    {
        _itemCatalog = itemCatalog;
        _generatorCatalog = generatorCatalog;
        _questCatalog = questCatalog;
    }

    public ContentCatalogResponse Execute()
    {
        var chains = _itemCatalog.GetAll()
            .GroupBy(value => value.ChainId, StringComparer.Ordinal)
            .Select(group => new ItemChainCatalogState(
                group.Key,
                group.OrderBy(value => value.Level)
                    .Select(value => new ItemCatalogState(
                        value.Level, value.Name, value.IsMaxLevel, value.SellPrice))
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
        var quests = _questCatalog.GetAll()
            .Select(value => new QuestCatalogState(
                value.QuestId,
                value.EventType,
                value.TargetCount,
                value.RewardCoins,
                value.PeriodType.ToString().ToLowerInvariant()))
            .OrderBy(value => value.QuestId, StringComparer.Ordinal)
            .ToArray();

        return new ContentCatalogResponse(
            GameContentVersion.Current,
            new BoardRulesState(PlayerBoard.Width, PlayerBoard.Height, PlayerBoard.SlotCount),
            new EconomyRulesState(
                PlayerEconomy.MaxEnergy,
                checked((int)PlayerEconomy.EnergyRechargeInterval.TotalSeconds),
                PlayerEconomy.DailyCoinReward,
                PlayerEconomy.FriendEnergyGiftAmount),
            new InventoryRulesState(PlayerInventory.InitialCapacity),
            chains,
            generators,
            quests);
    }
}

public sealed record ContentCatalogResponse(
    string Version,
    BoardRulesState Board,
    EconomyRulesState Economy,
    InventoryRulesState Inventory,
    IReadOnlyList<ItemChainCatalogState> ItemChains,
    IReadOnlyList<GeneratorCatalogState> Generators,
    IReadOnlyList<QuestCatalogState> Quests);

public sealed record BoardRulesState(int Width, int Height, int SlotCount);
public sealed record EconomyRulesState(
    int MaxEnergy,
    int EnergyRechargeSeconds,
    long DailyCoinReward,
    int FriendEnergyGiftAmount);
public sealed record InventoryRulesState(int InitialCapacity);
public sealed record ItemChainCatalogState(string ChainId, IReadOnlyList<ItemCatalogState> Levels);
public sealed record ItemCatalogState(int Level, string Name, bool IsMaxLevel, long SellPrice);
public sealed record GeneratorCatalogState(
    string GeneratorId,
    string GeneratedChainId,
    int GeneratedLevel,
    int EnergyCost,
    int MaxCharges,
    int ChargeRecoverySeconds);
public sealed record QuestCatalogState(
    string QuestId,
    string EventType,
    int TargetCount,
    long RewardCoins,
    string PeriodType);
