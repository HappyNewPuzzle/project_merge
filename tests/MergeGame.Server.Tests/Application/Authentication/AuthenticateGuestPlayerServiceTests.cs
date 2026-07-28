using MergeGame.Server.Application.Authentication;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Authentication;

/// <summary>
/// 게스트 자격 증명 검증과 JWT 발급 조건을 테스트합니다.
/// </summary>
public sealed class AuthenticateGuestPlayerServiceTests
{
    private const string RawGuestToken = "guest-token-known-only-to-client";

    /// <summary>
    /// 플레이어 ID와 원본 토큰이 모두 일치할 때만 JWT 발급 결과를 반환하는지 확인합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithValidCredentials_ReturnsAccessToken()
    {
        await using var dbContext = CreateDbContext();
        var player = CreatePlayer(RawGuestToken);
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();

        var tokenIssuer = new StubJwtTokenIssuer();
        var service = new AuthenticateGuestPlayerService(dbContext, tokenIssuer);

        var result = await service.ExecuteAsync(player.Id, RawGuestToken);

        Assert.NotNull(result);
        Assert.Equal(player.Id, result.PlayerId);
        Assert.Equal(StubJwtTokenIssuer.AccessToken, result.AccessToken);
        Assert.Equal(1, tokenIssuer.IssueCallCount);
    }

    /// <summary>
    /// 플레이어는 존재하지만 토큰이 다르면 로그인과 JWT 발급을 모두 거부하는지 확인합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithInvalidToken_ReturnsNullWithoutIssuingJwt()
    {
        await using var dbContext = CreateDbContext();
        var player = CreatePlayer(RawGuestToken);
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();

        var tokenIssuer = new StubJwtTokenIssuer();
        var service = new AuthenticateGuestPlayerService(dbContext, tokenIssuer);

        var result = await service.ExecuteAsync(player.Id, "wrong-token");

        Assert.Null(result);
        Assert.Equal(0, tokenIssuer.IssueCallCount);
    }

    /// <summary>
    /// 존재하지 않는 플레이어 ID도 토큰 오류와 동일한 실패 결과를 반환하는지 확인합니다.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithUnknownPlayer_ReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var tokenIssuer = new StubJwtTokenIssuer();
        var service = new AuthenticateGuestPlayerService(dbContext, tokenIssuer);

        var result = await service.ExecuteAsync(Guid.NewGuid(), RawGuestToken);

        Assert.Null(result);
        Assert.Equal(0, tokenIssuer.IssueCallCount);
    }

    private static MergeGameDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"guest-auth-{Guid.NewGuid()}")
            .Options;

        return new MergeGameDbContext(options);
    }

    private static Player CreatePlayer(string rawToken)
    {
        return Player.CreateGuest(
            Guid.NewGuid(),
            GuestTokenHasher.Hash(rawToken),
            DateTime.UtcNow);
    }

    /// <summary>
    /// 서비스가 성공한 경우에만 발급기를 호출했는지 추적하는 테스트 대역입니다.
    /// </summary>
    private sealed class StubJwtTokenIssuer : IJwtTokenIssuer
    {
        public const string AccessToken = "signed-jwt-for-test";
        public int IssueCallCount { get; private set; }

        public IssuedAccessToken Issue(Guid playerId)
        {
            IssueCallCount++;
            return new IssuedAccessToken(
                AccessToken,
                new DateTime(2026, 7, 28, 12, 15, 0, DateTimeKind.Utc));
        }
    }
}
