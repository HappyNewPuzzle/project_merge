using System.Security.Cryptography;
using System.Text;

namespace MergeGame.Server.Infrastructure.Security;

/// <summary>
/// 게스트 원본 토큰을 DB 저장 및 로그인 비교용 SHA-256 해시로 변환합니다.
/// </summary>
public static class GuestTokenHasher
{
    /// <summary>
    /// UTF-8 토큰의 SHA-256 해시를 64자리 대문자 16진수로 반환합니다.
    /// </summary>
    /// <param name="rawToken">클라이언트가 제출한 원본 게스트 토큰입니다.</param>
    /// <returns>Player.GuestTokenHash와 같은 형식의 해시 문자열입니다.</returns>
    public static string Hash(string rawToken)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(rawToken);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// 제출 토큰과 저장된 해시를 실행 시간 차이가 최소화되는 방식으로 비교합니다.
    /// </summary>
    /// <param name="rawToken">로그인 요청이 제출한 원본 토큰입니다.</param>
    /// <param name="storedHash">DB에 저장된 64자리 SHA-256 해시입니다.</param>
    /// <returns>두 값이 같은 토큰을 나타내면 true입니다.</returns>
    public static bool Matches(string rawToken, string storedHash)
    {
        if (storedHash.Length != 64)
        {
            return false;
        }

        var submittedHashBytes = Convert.FromHexString(Hash(rawToken));
        var storedHashBytes = Convert.FromHexString(storedHash);

        // 일반 문자열 비교보다 입력이 어느 위치에서 다른지에 따른 시간 차이가 작아 타이밍 공격을 완화합니다.
        return CryptographicOperations.FixedTimeEquals(
            submittedHashBytes,
            storedHashBytes);
    }
}
