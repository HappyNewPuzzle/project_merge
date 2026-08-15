namespace MergeGame.Server.Domain.Generators;

/// <summary>
/// 서버가 소유하는 생성기 규칙입니다. 클라이언트는 생성 아이템이나 비용을 지정할 수 없습니다.
/// </summary>
public sealed record GeneratorDefinition(
    string Id,
    string GeneratedChainId,
    int GeneratedLevel,
    int EnergyCost,
    int MaxCharges,
    TimeSpan ChargeRecoveryInterval);

/// <summary>
/// 라우트의 generatorId를 신뢰 가능한 서버 규칙으로 변환합니다.
/// </summary>
public interface IGeneratorCatalog
{
    bool TryGet(string generatorId, out GeneratorDefinition definition);

    /// <summary>부트스트랩과 콘텐츠 API가 현재 활성 생성기 전체를 안정된 순서로 조회합니다.</summary>
    IReadOnlyList<GeneratorDefinition> GetAll();
}
