using MergeGame.Server.Domain.Boards;

namespace MergeGame.Server.Infrastructure.Items;

/// <summary>
/// 현재 서버 빌드에 포함된 불변 아이템 정의 카탈로그입니다.
/// 초기 단계에는 코드로 버전을 관리하고, 라이브 밸런싱이 필요해지면 별도 관리 데이터로 이전할 수 있습니다.
/// </summary>
public sealed class InMemoryItemCatalog : IItemCatalog
{
    private static readonly IReadOnlyDictionary<(string ChainId, int Level), ItemDefinition>
        Definitions = CreateDefinitions();

    /// <inheritdoc />
    public bool TryGet(
        string chainId,
        int level,
        out ItemDefinition definition)
    {
        return Definitions.TryGetValue(
            (chainId.ToLowerInvariant(), level),
            out definition!);
    }

    /// <inheritdoc />
    public bool TryGetNext(
        string chainId,
        int currentLevel,
        out ItemDefinition nextDefinition)
    {
        return TryGet(chainId, currentLevel + 1, out nextDefinition);
    }

    public IReadOnlyList<ItemDefinition> GetAll() => Definitions.Values
        .OrderBy(value => value.ChainId, StringComparer.Ordinal)
        .ThenBy(value => value.Level)
        .ToArray();

    /// <summary>
    /// 첫 번째 머지 체인의 모든 단계를 한곳에서 정의합니다.
    /// 마지막 단계에는 IsMaxLevel을 표시해 의도하지 않은 다음 단계 추가를 방지합니다.
    /// </summary>
    private static IReadOnlyDictionary<(string, int), ItemDefinition> CreateDefinitions()
    {
        var definitions = new[]
        {
            new ItemDefinition("garden", 1, "Seed Bag", IsMaxLevel: false),
            new ItemDefinition("garden", 2, "Green Sprout", IsMaxLevel: false),
            new ItemDefinition("garden", 3, "Flower Pot", IsMaxLevel: false),
            new ItemDefinition("garden", 4, "Flower Basket", IsMaxLevel: false),
            new ItemDefinition("garden", 5, "Garden Arch", IsMaxLevel: true)
        };

        return definitions.ToDictionary(
            definition => (definition.ChainId, definition.Level));
    }
}
