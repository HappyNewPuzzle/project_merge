using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MergeGame.Server.Infrastructure.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace MergeGame.Server.Tests.Infrastructure.Authentication;

/// <summary>
/// JWT 발급기가 올바른 서명, 대상, 플레이어 클레임, 수명을 생성하는지 검증합니다.
/// </summary>
public sealed class JwtTokenIssuerTests
{
    private const string TestSigningKey =
        "test-only-signing-key-with-more-than-32-bytes";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 발급된 JWT의 서명을 실제로 검증하고 표준 sub 클레임과 15분 만료를 확인합니다.
    /// </summary>
    [Fact]
    public void Issue_ReturnsValidSignedTokenForPlayer()
    {
        var options = new JwtOptions
        {
            Issuer = "MergeGame.Server.Tests",
            Audience = "MergeGame.Client.Tests",
            SigningKey = TestSigningKey,
            AccessTokenMinutes = 15
        };
        var issuer = new JwtTokenIssuer(options, new StubTimeProvider(FixedNow));
        var playerId = Guid.NewGuid();

        var issuedToken = issuer.Issue(playerId);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(TestSigningKey)),
            // 테스트 고정 시각이 실행 시각과 달라도 서명과 클레임 검증에 집중하도록 수명 검증은 별도로 수행합니다.
            ValidateLifetime = false
        };

        var handler = new JwtSecurityTokenHandler
        {
            // 테스트에서도 운영 설정과 같이 표준 JWT 클레임 이름을 그대로 유지합니다.
            MapInboundClaims = false
        };
        var principal = handler.ValidateToken(
            issuedToken.Token,
            validationParameters,
            out var validatedToken);

        Assert.Equal(
            playerId.ToString(),
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.NotNull(principal.FindFirst(JwtRegisteredClaimNames.Jti));
        Assert.Equal(FixedNow.UtcDateTime.AddMinutes(15), issuedToken.ExpiresAtUtc);
        Assert.Equal(SecurityAlgorithms.HmacSha256, ((JwtSecurityToken)validatedToken).Header.Alg);
    }

    /// <summary>
    /// 토큰의 발급 시각을 재현 가능하게 만드는 고정 시간 공급자입니다.
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
