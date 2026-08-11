using MergeGame.Server.Domain.Generators;

namespace MergeGame.Server.Tests.Domain.Generators;

/// <summary>생성기 충전 소비와 서버 시간 기반 회복 계산을 검증합니다.</summary>
public sealed class PlayerGeneratorTests
{
    private static readonly GeneratorDefinition Definition =
        new("garden", "garden", 1, 5, TimeSpan.FromSeconds(30));

    [Fact]
    public void CreateSnapshot_AfterCooldownInterval_RecoversOneCharge()
    {
        var now = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);
        var generator = PlayerGenerator.CreateInitial(Guid.NewGuid(), Definition, now);
        for (var index = 0; index < Definition.MaxCharges; index++)
            Assert.True(generator.TryConsumeCharge(now, Definition));

        var snapshot = generator.CreateSnapshot(now.AddSeconds(30), Definition);

        Assert.Equal(1, snapshot.Charges);
        Assert.False(snapshot.IsCoolingDown);
        Assert.Equal(now.AddSeconds(60), snapshot.NextChargeAtUtc);
    }
}
