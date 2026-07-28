namespace MergeGame.Server.Domain.Boards;

/// <summary>
/// 플레이어 보드의 한 슬롯에 놓인 실제 아이템 인스턴스입니다.
/// </summary>
public sealed class BoardItem
{
    // EF Core가 DB 행을 객체로 복원할 때 사용합니다.
    private BoardItem()
    {
    }

    /// <summary>
    /// 아이템 인스턴스의 전역 고유 식별자입니다.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// 이 아이템을 소유한 플레이어이자 보드 식별자입니다.
    /// </summary>
    public Guid PlayerId { get; private set; }

    /// <summary>
    /// 0부터 34까지의 보드 슬롯 위치입니다.
    /// </summary>
    public int SlotIndex { get; private set; }

    /// <summary>
    /// 아이템이 속한 머지 계열 식별자입니다.
    /// </summary>
    public string ChainId { get; private set; } = string.Empty;

    /// <summary>
    /// 머지 계열 안의 현재 단계입니다.
    /// </summary>
    public int Level { get; private set; }

    /// <summary>
    /// 초기 보드 또는 서버 보상 시스템이 유효한 아이템을 생성할 때 사용합니다.
    /// </summary>
    internal static BoardItem Create(
        Guid playerId,
        int slotIndex,
        string chainId,
        int level)
    {
        return new BoardItem
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            SlotIndex = slotIndex,
            ChainId = chainId,
            Level = level
        };
    }

    /// <summary>
    /// 성공한 머지의 대상 아이템을 카탈로그가 결정한 다음 레벨로 올립니다.
    /// </summary>
    internal void UpgradeTo(int nextLevel)
    {
        Level = nextLevel;
    }
}
