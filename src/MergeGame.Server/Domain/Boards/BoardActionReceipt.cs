namespace MergeGame.Server.Domain.Boards;

/// <summary>
/// 성공한 통합 보드 액션의 최초 응답을 저장합니다.
/// 플레이어와 멱등 키의 DB 유니크 제약이 동시 재시도도 한 번만 반영되게 보장합니다.
/// </summary>
public sealed class BoardActionReceipt
{
    private BoardActionReceipt() { }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int SourceSlot { get; private set; }
    public int TargetSlot { get; private set; }
    public string ResponseJson { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static BoardActionReceipt Create(
        Guid playerId,
        string idempotencyKey,
        int sourceSlot,
        int targetSlot,
        string responseJson,
        DateTime nowUtc) => new()
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            IdempotencyKey = idempotencyKey,
            SourceSlot = sourceSlot,
            TargetSlot = targetSlot,
            ResponseJson = responseJson,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
        };
}
