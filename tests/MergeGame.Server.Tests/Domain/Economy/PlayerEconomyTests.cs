using MergeGame.Server.Domain.Economy;

namespace MergeGame.Server.Tests.Domain.Economy;

/// <summary>
/// 서버 시간 기반 에너지와 일일 보상 규칙을 검증합니다.
/// </summary>
public sealed class PlayerEconomyTests
{
    private static readonly DateTime Start =
        new(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 최대 에너지 동안 지난 시간을 비축하지 않고 소비 시점부터 5분 후 충전되는지 확인합니다.
    /// </summary>
    [Fact]
    public void SpendFromFullEnergy_DoesNotUseStoredRechargeTime()
    {
        var economy = PlayerEconomy.CreateInitial(Guid.NewGuid(), Start);
        var spendAt = Start.AddHours(10);

        var error = economy.TrySpendGeneratorEnergy(1, spendAt);
        var beforeRecharge = economy.CreateSnapshot(spendAt.AddMinutes(4));
        var afterRecharge = economy.CreateSnapshot(spendAt.AddMinutes(5));

        Assert.Equal(EconomyActionError.None, error);
        Assert.Equal(99, beforeRecharge.Energy);
        Assert.Equal(100, afterRecharge.Energy);
        Assert.Equal(2, economy.Revision);
    }

    /// <summary>
    /// 같은 UTC 날짜에는 일일 코인이 한 번만 지급되고 revision도 한 번만 증가하는지 확인합니다.
    /// </summary>
    [Fact]
    public void ClaimDailyReward_TwiceSameUtcDate_RejectsSecondClaim()
    {
        var economy = PlayerEconomy.CreateInitial(Guid.NewGuid(), Start);

        var first = economy.TryClaimDailyReward(1, Start.AddHours(1));
        var second = economy.TryClaimDailyReward(2, Start.AddHours(20));
        var snapshot = economy.CreateSnapshot(Start.AddHours(20));

        Assert.Equal(EconomyActionError.None, first);
        Assert.Equal(EconomyActionError.DailyRewardAlreadyClaimed, second);
        Assert.Equal(PlayerEconomy.DailyCoinReward, snapshot.Coins);
        Assert.Equal(2, snapshot.Revision);
    }

    /// <summary>
    /// 다음 UTC 날짜가 되면 다시 일일 보상을 받을 수 있는지 확인합니다.
    /// </summary>
    [Fact]
    public void ClaimDailyReward_NextUtcDate_SucceedsAgain()
    {
        var economy = PlayerEconomy.CreateInitial(Guid.NewGuid(), Start);
        economy.TryClaimDailyReward(1, Start);

        var result = economy.TryClaimDailyReward(2, Start.AddDays(1));
        var snapshot = economy.CreateSnapshot(Start.AddDays(1));

        Assert.Equal(EconomyActionError.None, result);
        Assert.Equal(PlayerEconomy.DailyCoinReward * 2, snapshot.Coins);
        Assert.Equal(3, snapshot.Revision);
    }
}
