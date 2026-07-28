namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>
/// JWT 발급과 검증 양쪽에서 공유하는 설정입니다.
/// 발급자, 대상, 서명 키가 다르면 다른 서비스가 만든 토큰을 실수로 신뢰하지 않습니다.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// 토큰을 발급한 서버를 나타내는 고정 문자열입니다.
    /// </summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>
    /// 토큰을 사용할 대상 클라이언트 또는 API를 나타냅니다.
    /// </summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// HMAC SHA-256 서명에 사용하는 최소 32바이트 비밀 키입니다.
    /// 소스 저장소가 아닌 환경 변수나 비밀 저장소에서 주입해야 합니다.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// 액세스 토큰의 유효 시간(분)입니다. 탈취 피해를 제한하도록 짧게 유지합니다.
    /// </summary>
    public int AccessTokenMinutes { get; init; } = 15;
}
