namespace MergeGame.Server.Domain.Boards;

/// <summary>성공한 아이템 판매 응답을 저장해 재시도 시 아이템 제거와 코인 지급을 반복하지 않습니다.</summary>
public sealed class BoardItemSaleReceipt
{
    private BoardItemSaleReceipt() { }
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid ItemId { get; private set; }
    public string ResponseJson { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static BoardItemSaleReceipt Create(
        Guid playerId,
        string idempotencyKey,
        Guid itemId,
        string responseJson,
        DateTime nowUtc) => new()
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            IdempotencyKey = idempotencyKey,
            ItemId = itemId,
            ResponseJson = responseJson,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
        };
}
