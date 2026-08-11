namespace MergeGame.Server.Domain.Generators;

/// <summary>
/// 성공한 생성 요청의 응답을 보관하는 멱등 영수증입니다.
/// (player_id, idempotency_key) 유니크 키가 동시 재시도까지 한 번만 성공하게 보장합니다.
/// </summary>
public sealed class GeneratorProductionReceipt
{
    private GeneratorProductionReceipt()
    {
    }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string GeneratorId { get; private set; } = string.Empty;
    public string ResponseJson { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static GeneratorProductionReceipt Create(
        Guid playerId,
        string idempotencyKey,
        string generatorId,
        string responseJson,
        DateTime nowUtc) => new()
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            IdempotencyKey = idempotencyKey,
            GeneratorId = generatorId,
            ResponseJson = responseJson,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
        };
}
