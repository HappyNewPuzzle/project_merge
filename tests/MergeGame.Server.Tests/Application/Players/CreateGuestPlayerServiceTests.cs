using MergeGame.Server.Application.Players;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Players;

/// <summary>
/// 게스트 생성 유스케이스가 보안 경계와 DB 저장 규칙을 지키는지 검증합니다.
/// </summary>
public sealed class CreateGuestPlayerServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 원본 토큰은 응답에만 존재하고 데이터베이스에는 해시만 저장되는지 확인합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_StoresHashAndReturnsRawToken()
    {
        // 테스트마다 고유한 DB 이름을 사용해 병렬 실행 시 데이터가 섞이지 않게 합니다.
        var options = new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"guest-player-{Guid.NewGuid()}")
            .Options;

        await using var dbContext = new MergeGameDbContext(options);
        var credentialGenerator = new StubCredentialGenerator();
        var timeProvider = new StubTimeProvider(FixedNow);
        var service = new CreateGuestPlayerService(
            dbContext,
            credentialGenerator,
            timeProvider);

        var result = await service.ExecuteAsync();
        var savedPlayer = await dbContext.Players.SingleAsync();

        Assert.Equal(StubCredentialGenerator.RawToken, result.GuestToken);
        Assert.Equal(StubCredentialGenerator.TokenHash, savedPlayer.GuestTokenHash);
        Assert.DoesNotContain(result.GuestToken, savedPlayer.GuestTokenHash);
        Assert.Equal(FixedNow.UtcDateTime, savedPlayer.CreatedAtUtc);
        Assert.Equal($"Guest-{savedPlayer.Id:N}"[..14], savedPlayer.DisplayName);
    }

    /// <summary>
    /// 테스트가 난수에 의존하지 않도록 정해진 자격 증명을 반환합니다.
    /// </summary>
    private sealed class StubCredentialGenerator : IGuestCredentialGenerator
    {
        public const string RawToken = "raw-token-returned-once";
        public const string TokenHash =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        public GuestCredential Generate() => new(RawToken, TokenHash);
    }

    /// <summary>
    /// 계정 생성 시각을 정확히 검증할 수 있도록 고정된 UTC 시각을 제공합니다.
    /// </summary>
    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public StubTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
