namespace MergeGame.Server.Domain.Boards;

/// <summary>소유 인스턴스와 분리된 영구 발견 기록입니다. 소비·판매로 삭제하지 않습니다.</summary>
public sealed class ItemDiscovery
{
    private ItemDiscovery() { }
    public Guid PlayerId { get; private set; }
    public string ChainId { get; private set; } = string.Empty;
    public int Level { get; private set; }

    internal static ItemDiscovery Create(Guid playerId, string chainId, int level) =>
        new() { PlayerId = playerId, ChainId = chainId, Level = level };
}
