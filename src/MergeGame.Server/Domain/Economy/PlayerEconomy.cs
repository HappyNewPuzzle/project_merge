namespace MergeGame.Server.Domain.Economy;

/// <summary>
/// 플레이어의 에너지, 코인, 시간 기반 충전 기준과 보상 이력을 관리합니다.
/// 클라이언트 시각이나 재화 계산값은 신뢰하지 않습니다.
/// </summary>
public sealed class PlayerEconomy
{
    public const int MaxEnergy = 100;
    public const int GeneratorEnergyCost = 1;
    public const int DailyCoinReward = 50;
    public const int FriendEnergyGiftAmount = 5;
    public static readonly TimeSpan EnergyRechargeInterval = TimeSpan.FromMinutes(5);

    private PlayerEconomy()
    {
    }

    public Guid PlayerId { get; private set; }
    public int Energy { get; private set; }
    public long Coins { get; private set; }
    public long Revision { get; private set; }
    public DateTime LastEnergyUpdatedAtUtc { get; private set; }
    public DateTime? LastDailyRewardClaimedAtUtc { get; private set; }

    /// <summary>
    /// 신규 플레이어에게 최대 에너지와 코인 0의 경제 상태를 생성합니다.
    /// </summary>
    public static PlayerEconomy CreateInitial(Guid playerId, DateTime nowUtc)
    {
        return new PlayerEconomy
        {
            PlayerId = playerId,
            Energy = MaxEnergy,
            Coins = 0,
            Revision = 1,
            LastEnergyUpdatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// revision과 서버 시간 충전을 적용한 뒤 생성기 에너지 1을 소비합니다.
    /// </summary>
    public EconomyActionError TrySpendGeneratorEnergy(
        long expectedRevision,
        DateTime nowUtc)
    {
        if (expectedRevision != Revision)
        {
            return EconomyActionError.StaleRevision;
        }

        ApplyEnergyRecharge(nowUtc);
        if (Energy < GeneratorEnergyCost)
        {
            return EconomyActionError.InsufficientEnergy;
        }

        Energy -= GeneratorEnergyCost;
        Revision++;
        return EconomyActionError.None;
    }

    /// <summary>
    /// UTC 날짜 기준 하루 한 번 코인 보상을 지급합니다.
    /// </summary>
    public EconomyActionError TryClaimDailyReward(
        long expectedRevision,
        DateTime nowUtc)
    {
        if (expectedRevision != Revision)
        {
            return EconomyActionError.StaleRevision;
        }

        var utcNow = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        if (LastDailyRewardClaimedAtUtc?.Date == utcNow.Date)
        {
            return EconomyActionError.DailyRewardAlreadyClaimed;
        }

        Coins += DailyCoinReward;
        LastDailyRewardClaimedAtUtc = utcNow;
        Revision++;
        return EconomyActionError.None;
    }

    /// <summary>
    /// 검증 완료된 서버 보상을 revision 조건과 함께 코인에 반영합니다.
    /// </summary>
    public EconomyActionError TryCreditCoins(
        long expectedRevision,
        long amount)
    {
        if (expectedRevision != Revision)
        {
            return EconomyActionError.StaleRevision;
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        checked
        {
            Coins += amount;
        }
        Revision++;
        return EconomyActionError.None;
    }

    /// <summary>서버 시간을 기준으로 자연 충전을 먼저 반영한 뒤 친구 선물 에너지를 더합니다.</summary>
    public EconomyActionError TryReceiveFriendEnergy(DateTime nowUtc)
    {
        ApplyEnergyRecharge(nowUtc);
        if (Energy >= MaxEnergy)
            return EconomyActionError.EnergyAlreadyFull;

        Energy = Math.Min(MaxEnergy, Energy + FriendEnergyGiftAmount);
        Revision++;
        return EconomyActionError.None;
    }

    /// <summary>
    /// DB 상태를 변경하지 않고 현재 서버 시각 기준으로 보이는 에너지와 다음 충전 시각을 계산합니다.
    /// </summary>
    public EconomySnapshot CreateSnapshot(DateTime nowUtc)
    {
        var (projectedEnergy, projectedAnchor) = CalculateRecharge(nowUtc);
        DateTime? nextEnergyAtUtc = projectedEnergy >= MaxEnergy
            ? null
            : projectedAnchor.Add(EnergyRechargeInterval);

        return new EconomySnapshot(
            PlayerId,
            projectedEnergy,
            MaxEnergy,
            Coins,
            Revision,
            nextEnergyAtUtc,
            LastDailyRewardClaimedAtUtc?.Date == nowUtc.Date);
    }

    private void ApplyEnergyRecharge(DateTime nowUtc)
    {
        var (rechargedEnergy, updatedAnchor) = CalculateRecharge(nowUtc);
        Energy = rechargedEnergy;
        LastEnergyUpdatedAtUtc = updatedAnchor;
    }

    private (int Energy, DateTime Anchor) CalculateRecharge(DateTime nowUtc)
    {
        var utcNow = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        if (Energy >= MaxEnergy)
        {
            // 최대치에서 시간을 비축했다가 소비 직후 한꺼번에 충전하는 현상을 방지합니다.
            return (MaxEnergy, utcNow);
        }

        var elapsed = utcNow - LastEnergyUpdatedAtUtc;
        if (elapsed <= TimeSpan.Zero)
        {
            return (Energy, LastEnergyUpdatedAtUtc);
        }

        var intervals = (int)(elapsed.Ticks / EnergyRechargeInterval.Ticks);
        if (intervals <= 0)
        {
            return (Energy, LastEnergyUpdatedAtUtc);
        }

        var recharged = Math.Min(MaxEnergy, Energy + intervals);
        var anchor = recharged >= MaxEnergy
            ? utcNow
            : LastEnergyUpdatedAtUtc.AddTicks(
                EnergyRechargeInterval.Ticks * intervals);
        return (recharged, anchor);
    }
}

public enum EconomyActionError
{
    None,
    StaleRevision,
    InsufficientEnergy,
    DailyRewardAlreadyClaimed,
    EnergyAlreadyFull
}

public sealed record EconomySnapshot(
    Guid PlayerId,
    int Energy,
    int MaxEnergy,
    long Coins,
    long Revision,
    DateTime? NextEnergyAtUtc,
    bool DailyRewardClaimedToday);
