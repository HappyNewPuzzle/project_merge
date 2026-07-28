using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>
/// HMAC SHA-256으로 서명된 플레이어 액세스 JWT를 생성합니다.
/// </summary>
public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;

    /// <summary>
    /// 검증된 JWT 설정과 테스트 가능한 시간 공급자를 주입받습니다.
    /// </summary>
    public JwtTokenIssuer(JwtOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public IssuedAccessToken Issue(Guid playerId)
    {
        var issuedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = issuedAtUtc.AddMinutes(_options.AccessTokenMinutes);

        var claims = new[]
        {
            // sub는 JWT 표준 주체 클레임이며 인증된 플레이어 ID만 기록합니다.
            new Claim(JwtRegisteredClaimNames.Sub, playerId.ToString()),
            // jti는 개별 토큰을 구분해 향후 폐기 목록이나 감사 로그에 활용할 수 있습니다.
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAtUtc,
            expires: expiresAtUtc,
            signingCredentials: _signingCredentials);

        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAtUtc);
    }
}
