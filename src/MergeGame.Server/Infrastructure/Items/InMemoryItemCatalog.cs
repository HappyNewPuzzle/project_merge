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
    /// Production Generator가 출력하는 모든 머지 체인을 한곳에서 정의합니다.
    /// 각 체인의 마지막 단계에만 IsMaxLevel을 표시해 존재하지 않는 다음 단계 생성을 방지합니다.
    /// </summary>
    private static IReadOnlyDictionary<(string, int), ItemDefinition> CreateDefinitions()
    {
        var definitions = new[]
        {
            new ItemDefinition("garden", 1, "Seed Bag", IsMaxLevel: false, SellPrice: 5),
            new ItemDefinition("garden", 2, "Green Sprout", IsMaxLevel: false, SellPrice: 10),
            new ItemDefinition("garden", 3, "Flower Pot", IsMaxLevel: false, SellPrice: 20),
            new ItemDefinition("garden", 4, "Flower Basket", IsMaxLevel: false, SellPrice: 40),
            new ItemDefinition("garden", 5, "Garden Arch", IsMaxLevel: true, SellPrice: 80),

            // Unity 리소스 이름은 서버 계약에 포함하지 않습니다. 아래 이름은 표시용 콘텐츠 메타데이터이며,
            // 생성과 머지 판정은 안정적인 chainId와 level만 사용합니다.
            new ItemDefinition("toy", 1, "Toy Block", IsMaxLevel: false, SellPrice: 5),
            new ItemDefinition("toy", 2, "Toy Car", IsMaxLevel: false, SellPrice: 10),
            new ItemDefinition("toy", 3, "Teddy Bear", IsMaxLevel: false, SellPrice: 20),
            new ItemDefinition("toy", 4, "Toy Robot", IsMaxLevel: false, SellPrice: 40),
            new ItemDefinition("toy", 5, "Dollhouse", IsMaxLevel: false, SellPrice: 80),
            new ItemDefinition("toy", 6, "Train Set", IsMaxLevel: false, SellPrice: 160),
            new ItemDefinition("toy", 7, "Arcade Machine", IsMaxLevel: false, SellPrice: 320),
            new ItemDefinition("toy", 8, "Grand Toy Store", IsMaxLevel: true, SellPrice: 640),

            new ItemDefinition("food", 1, "Flour Bag", IsMaxLevel: false, SellPrice: 5),
            new ItemDefinition("food", 2, "Fresh Bread", IsMaxLevel: false, SellPrice: 10),
            new ItemDefinition("food", 3, "Sandwich", IsMaxLevel: false, SellPrice: 20),
            new ItemDefinition("food", 4, "Lunch Box", IsMaxLevel: false, SellPrice: 40),
            new ItemDefinition("food", 5, "Pizza", IsMaxLevel: false, SellPrice: 80),
            new ItemDefinition("food", 6, "Family Feast", IsMaxLevel: false, SellPrice: 160),
            new ItemDefinition("food", 7, "Chef's Table", IsMaxLevel: false, SellPrice: 320),
            new ItemDefinition("food", 8, "Grand Banquet", IsMaxLevel: true, SellPrice: 640),

            new ItemDefinition("rest", 1, "Pillow", IsMaxLevel: false, SellPrice: 5),
            new ItemDefinition("rest", 2, "Soft Blanket", IsMaxLevel: false, SellPrice: 10),
            new ItemDefinition("rest", 3, "Armchair", IsMaxLevel: false, SellPrice: 20),
            new ItemDefinition("rest", 4, "Cozy Sofa", IsMaxLevel: false, SellPrice: 40),
            new ItemDefinition("rest", 5, "Comfy Bed", IsMaxLevel: false, SellPrice: 80),
            new ItemDefinition("rest", 6, "Luxury Bed", IsMaxLevel: false, SellPrice: 160),
            new ItemDefinition("rest", 7, "Dream Suite", IsMaxLevel: false, SellPrice: 320),
            new ItemDefinition("rest", 8, "Royal Retreat", IsMaxLevel: true, SellPrice: 640)
        };

        return definitions.ToDictionary(
            definition => (definition.ChainId, definition.Level));
    }
}
