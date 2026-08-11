using MergeGame.Server.Domain.Generators;

namespace MergeGame.Server.Infrastructure.Generators;

/// <summary>
/// 현재 버전에서 허용하는 생성기 정의입니다. 서버 배포 없이 밸런스를 운영해야 할 시점에는
/// 동일 인터페이스의 DB/원격 설정 구현으로 교체할 수 있습니다.
/// </summary>
public sealed class InMemoryGeneratorCatalog : IGeneratorCatalog
{
    private static readonly GeneratorDefinition Garden = new(
        Id: "garden",
        GeneratedChainId: "garden",
        GeneratedLevel: 1,
        MaxCharges: 5,
        ChargeRecoveryInterval: TimeSpan.FromSeconds(30));

    public bool TryGet(string generatorId, out GeneratorDefinition definition)
    {
        if (string.Equals(generatorId, Garden.Id, StringComparison.Ordinal))
        {
            definition = Garden;
            return true;
        }

        definition = null!;
        return false;
    }
}
