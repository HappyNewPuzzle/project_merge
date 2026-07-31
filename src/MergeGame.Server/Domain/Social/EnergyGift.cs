namespace MergeGame.Server.Domain.Social;

/// <summary>UTC 날짜별 에너지 선물 지급 증적입니다.</summary>
public sealed class EnergyGift
{
    private EnergyGift() { }

    public Guid Id { get; private set; }
    public Guid SenderPlayerId { get; private set; }
    public Guid RecipientPlayerId { get; private set; }
    public DateTime GiftDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static EnergyGift Create(Guid senderId, Guid recipientId, DateTime nowUtc)
    {
        if (senderId == recipientId) throw new InvalidOperationException("자신에게 에너지를 선물할 수 없습니다.");
        var utc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        return new EnergyGift
        {
            Id = Guid.NewGuid(), SenderPlayerId = senderId, RecipientPlayerId = recipientId,
            GiftDateUtc = utc.Date, CreatedAtUtc = utc
        };
    }
}
