using System.IdentityModel.Tokens.Jwt;

namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>
/// ASP.NET Core HttpContext의 검증 완료된 User 클레임에서 플레이어 ID를 읽습니다.
/// </summary>
public sealed class CurrentPlayerAccessor : ICurrentPlayerAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// 현재 요청에 접근하기 위한 프레임워크 접근자를 주입받습니다.
    /// </summary>
    public CurrentPlayerAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public bool TryGetPlayerId(out Guid playerId)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var subject = user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        return Guid.TryParse(subject, out playerId);
    }
}
