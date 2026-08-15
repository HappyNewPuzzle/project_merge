namespace MergeGame.Server.Domain.Economy;

/// <summary>
/// 경제 잔액을 변경한 사실을 삭제·수정하지 않는 영구 원장 행입니다.
/// 현재 잔액은 PlayerEconomy가 빠르게 제공하고, 원장은 원인 추적과 장애 복구 근거를 제공합니다.
/// </summary>
public sealed class EconomyLedgerEntry
{
    private EconomyLedgerEntry() { }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string Resource { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public long Delta { get; private set; }
    public long BalanceAfter { get; private set; }
    public long EconomyRevision { get; private set; }
    public string ReferenceId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    public static EconomyLedgerEntry CreateEnergy(
        Guid playerId,
        string reason,
        int delta,
        int balanceAfter,
        long economyRevision,
        string referenceId,
        DateTime occurredAtUtc) => Create(
            playerId, "energy", reason, delta, balanceAfter, economyRevision, referenceId, occurredAtUtc);

    public static EconomyLedgerEntry CreateCoins(
        Guid playerId,
        string reason,
        long delta,
        long balanceAfter,
        long economyRevision,
        string referenceId,
        DateTime occurredAtUtc) => Create(
            playerId, "coins", reason, delta, balanceAfter, economyRevision, referenceId, occurredAtUtc);

    private static EconomyLedgerEntry Create(
        Guid playerId,
        string resource,
        string reason,
        long delta,
        long balanceAfter,
        long economyRevision,
        string referenceId,
        DateTime occurredAtUtc)
    {
        if (delta == 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (balanceAfter < 0) throw new ArgumentOutOfRangeException(nameof(balanceAfter));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("원장 사유가 필요합니다.", nameof(reason));
        if (string.IsNullOrWhiteSpace(referenceId)) throw new ArgumentException("원장 참조 ID가 필요합니다.", nameof(referenceId));

        return new EconomyLedgerEntry
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Resource = resource,
            Reason = reason,
            Delta = delta,
            BalanceAfter = balanceAfter,
            EconomyRevision = economyRevision,
            ReferenceId = referenceId,
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc)
        };
    }
}
