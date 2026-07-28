namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>
/// 인증된 플레이어에게 서명된 액세스 토큰을 발급하는 계약입니다.
/// </summary>
public interface IJwtTokenIssuer
{
    /// <summary>
    /// 지정한 플레이어를 주체로 하는 짧은 수명의 JWT를 발급합니다.
    /// </summary>
    /// <param name="playerId">JWT의 sub 클레임에 기록할 플레이어 식별자입니다.</param>
    /// <returns>직렬화된 토큰과 UTC 만료 시각입니다.</returns>
    IssuedAccessToken Issue(Guid playerId);
}

/// <summary>
/// 클라이언트에 반환할 액세스 토큰 발급 결과입니다.
/// </summary>
/// <param name="Token">Authorization Bearer 헤더에 사용할 JWT 문자열입니다.</param>
/// <param name="ExpiresAtUtc">클라이언트가 재로그인 시점을 판단할 UTC 만료 시각입니다.</param>
public sealed record IssuedAccessToken(string Token, DateTime ExpiresAtUtc);
