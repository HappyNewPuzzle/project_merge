using MergeGame.Server.Application.Authentication;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Authentication;

/// <summary>토큰 회전과 재사용 탐지 시 계열 전체 폐기를 검증합니다.</summary>
public sealed class RefreshTokenServicesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RotateThenReuseOldToken_RevokesReplacementFamily()
    {
        await using var db = new MergeGameDbContext(new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"refresh-{Guid.NewGuid()}").Options);
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), Now.UtcDateTime);
        db.Players.Add(player); await db.SaveChangesAsync();
        var generator = new StubGenerator("first-refresh", "second-refresh");
        var options = new JwtOptions { RefreshTokenDays = 30 };
        var time = new StubTimeProvider();
        var created = await new CreateRefreshSessionService(db, generator, options, time).ExecuteAsync(player.Id);
        db.ChangeTracker.Clear();
        var rotate = new RotateRefreshTokenService(db, generator, new StubJwtIssuer(), options, time);

        var succeeded = await rotate.ExecuteAsync(created.RefreshToken);
        db.ChangeTracker.Clear();
        var reuse = await rotate.ExecuteAsync(created.RefreshToken);
        db.ChangeTracker.Clear();
        var sessions = await db.RefreshTokenSessions.OrderBy(x => x.CreatedAtUtc).ToListAsync();

        Assert.Equal(TokenRotationStatus.Succeeded, succeeded.Status);
        Assert.Equal("second-refresh", succeeded.Tokens!.RefreshToken);
        Assert.Equal(TokenRotationStatus.ReuseDetected, reuse.Status);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.NotNull(session.RevokedAtUtc));
    }

    private sealed class StubGenerator : IRefreshTokenGenerator
    {
        private readonly Queue<string> _tokens;
        public StubGenerator(params string[] tokens) => _tokens = new(tokens);
        public GeneratedRefreshToken Generate() { var raw = _tokens.Dequeue(); return new(raw, GuestTokenHasher.Hash(raw)); }
    }
    private sealed class StubJwtIssuer : IJwtTokenIssuer
    { public IssuedAccessToken Issue(Guid playerId) => new("access-token", Now.UtcDateTime.AddMinutes(15)); }
    private sealed class StubTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
