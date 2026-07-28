namespace MergeGame.Server.Infrastructure.Security;

/// <summary>
/// 게스트 로그인에 사용할 원본 토큰과 저장용 해시를 생성하는 계약입니다.
/// </summary>
public interface IGuestCredentialGenerator
{
    /// <summary>
    /// 암호학적으로 안전한 새 게스트 자격 증명을 생성합니다.
    /// </summary>
    /// <returns>클라이언트용 원본 토큰과 서버 저장용 해시입니다.</returns>
    GuestCredential Generate();
}

/// <summary>
/// 게스트 인증에 필요한 한 쌍의 값을 담습니다.
/// </summary>
/// <param name="RawToken">생성 응답에서만 클라이언트에 전달하는 원본 토큰입니다.</param>
/// <param name="TokenHash">데이터베이스에 저장할 SHA-256 해시입니다.</param>
public sealed record GuestCredential(string RawToken, string TokenHash);
