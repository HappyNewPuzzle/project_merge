using System.Security.Cryptography;
using System.Text;

namespace MergeGame.Server.Infrastructure.Security;

/// <summary>
/// 운영 환경에서 사용할 암호학적으로 안전한 게스트 자격 증명 생성기입니다.
/// </summary>
public sealed class GuestCredentialGenerator : IGuestCredentialGenerator
{
    private const int TokenByteLength = 32;

    /// <inheritdoc />
    public GuestCredential Generate()
    {
        // 256비트 난수는 추측 공격이 현실적으로 불가능한 충분한 엔트로피를 제공합니다.
        var randomBytes = RandomNumberGenerator.GetBytes(TokenByteLength);

        // URL-safe Base64는 토큰을 HTTP 헤더나 JSON에 넣을 때 +, /, = 문자의 처리가 필요 없습니다.
        var rawToken = Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        // DB가 유출돼도 원본 토큰을 바로 사용할 수 없도록 해시만 저장합니다.
        // 토큰 자체의 엔트로피가 충분히 높으므로 사용자 비밀번호용 느린 KDF 대신 빠른 SHA-256이 적합합니다.
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var tokenHash = Convert.ToHexString(hashBytes);

        return new GuestCredential(rawToken, tokenHash);
    }
}
