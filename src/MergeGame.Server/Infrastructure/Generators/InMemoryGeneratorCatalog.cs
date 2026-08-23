using MergeGame.Server.Domain.Generators;

namespace MergeGame.Server.Infrastructure.Generators;

/// <summary>
/// 현재 버전에서 허용하는 생성기 정의입니다. 서버 배포 없이 밸런스를 운영해야 할 시점에는
/// 동일 인터페이스의 DB/원격 설정 구현으로 교체할 수 있습니다.
/// </summary>
public sealed class InMemoryGeneratorCatalog : IGeneratorCatalog
{
    // 각 생성기를 별도 정의로 보관합니다. 현재 초기 밸런스 값은 같지만 한 생성기의 비용이나
    // 회복 속도를 조정할 때 다른 생성기에 의도하지 않은 변경이 전파되지 않습니다.
    private static readonly IReadOnlyList<GeneratorDefinition> Definitions =
    [
        CreateDefinition("garden", "garden"),
        CreateDefinition("toy_basic", "toy"),
        CreateDefinition("food_basic", "food"),
        CreateDefinition("rest_basic", "rest")
    ];

    // ID 비교는 DB의 ascii_bin collation과 같은 대소문자 구분 규칙을 사용합니다.
    // 잘못된 대소문자를 암묵적으로 허용하지 않아 콘텐츠 ID를 모든 환경에서 안정적으로 유지합니다.
    private static readonly IReadOnlyDictionary<string, GeneratorDefinition> DefinitionsById =
        Definitions.ToDictionary(value => value.Id, StringComparer.Ordinal);

    public bool TryGet(string generatorId, out GeneratorDefinition definition)
        => DefinitionsById.TryGetValue(generatorId, out definition!);

    public IReadOnlyList<GeneratorDefinition> GetAll() => Definitions;

    /// <summary>
    /// 기존 garden에서 검증된 초기 운영값을 적용합니다. 반환되는 각 record는 독립 인스턴스이므로
    /// 향후 Generator별 라이브 밸런스를 변경할 때 이 팩터리 호출을 개별 값으로 교체할 수 있습니다.
    /// </summary>
    private static GeneratorDefinition CreateDefinition(string id, string outputChainId) => new(
        Id: id,
        GeneratedChainId: outputChainId,
        GeneratedLevel: 1,
        EnergyCost: 1,
        MaxCharges: 5,
        ChargeRecoveryInterval: TimeSpan.FromSeconds(30));
}
