using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Social;
using MergeGame.Server.Infrastructure.Social;

namespace MergeGame.Server.Tests.Domain.Social;

/// <summary>DB나 HTTP와 무관한 소셜 불변 규칙을 빠르게 검증합니다.</summary>
public sealed class SocialDomainTests
{
    [Fact]
    public void Friendship_Create_NormalizesPlayerOrder()
    {
        var first = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var friendship = Friendship.Create(first, second, DateTime.UtcNow);

        Assert.Equal(second, friendship.PlayerLowId);
        Assert.Equal(first, friendship.PlayerHighId);
        Assert.Equal(first, friendship.GetOtherPlayerId(second));
    }

    [Fact]
    public void TryReceiveFriendEnergy_WhenBelowMaximum_CreditsFiveAndIncrementsRevision()
    {
        var now = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);
        var economy = PlayerEconomy.CreateInitial(Guid.NewGuid(), now);
        Assert.Equal(EconomyActionError.None, economy.TrySpendGeneratorEnergy(1, now));

        var result = economy.TryReceiveFriendEnergy(now);

        Assert.Equal(EconomyActionError.None, result);
        Assert.Equal(100, economy.Energy);
        Assert.Equal(3, economy.Revision);
    }

    [Fact]
    public void FriendCodeGenerator_GeneratesEightSafeUppercaseCharacters()
    {
        var code = new FriendCodeGenerator().Generate();
        Assert.Matches("^[2-9A-HJ-NP-Z]{8}$", code);
    }
}
