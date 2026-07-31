namespace MergeGame.Server.Domain.Social;

/// <summary>
/// 두 플레이어의 수락된 친구 관계입니다. 작은 GUID를 항상 왼쪽에 저장하여 A→B와
/// B→A가 별도 행으로 생기는 것을 데이터베이스 고유 제약으로 차단합니다.
/// </summary>
public sealed class Friendship
{
    private Friendship() { }

    public Guid Id { get; private set; }
    public Guid PlayerLowId { get; private set; }
    public Guid PlayerHighId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Friendship Create(Guid firstPlayerId, Guid secondPlayerId, DateTime createdAtUtc)
    {
        if (firstPlayerId == secondPlayerId) throw new InvalidOperationException("자기 자신은 친구로 추가할 수 없습니다.");
        var firstIsLow = firstPlayerId.CompareTo(secondPlayerId) < 0;
        return new Friendship
        {
            Id = Guid.NewGuid(),
            PlayerLowId = firstIsLow ? firstPlayerId : secondPlayerId,
            PlayerHighId = firstIsLow ? secondPlayerId : firstPlayerId,
            CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)
        };
    }

    public bool Contains(Guid playerId) => PlayerLowId == playerId || PlayerHighId == playerId;
    public Guid GetOtherPlayerId(Guid playerId) => PlayerLowId == playerId
        ? PlayerHighId
        : PlayerHighId == playerId ? PlayerLowId : throw new InvalidOperationException("친구 관계에 포함되지 않은 플레이어입니다.");
}
