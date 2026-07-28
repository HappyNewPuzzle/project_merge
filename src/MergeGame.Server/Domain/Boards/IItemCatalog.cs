namespace MergeGame.Server.Domain.Boards;

/// <summary>
/// 서버가 알고 있는 아이템 정의를 조회하는 계약입니다.
/// </summary>
public interface IItemCatalog
{
    /// <summary>
    /// 지정한 계열과 레벨의 정의를 조회합니다.
    /// </summary>
    bool TryGet(string chainId, int level, out ItemDefinition definition);

    /// <summary>
    /// 현재 정의를 머지했을 때 만들어지는 다음 단계 정의를 조회합니다.
    /// </summary>
    bool TryGetNext(
        string chainId,
        int currentLevel,
        out ItemDefinition nextDefinition);
}
