using System.Security.Cryptography;

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

        // 생성과 로그인 검증이 같은 해시 규칙을 사용하도록 공용 해시 함수에 위임합니다.
        var tokenHash = GuestTokenHasher.Hash(rawToken);

        return new GuestCredential(rawToken, tokenHash);
    }
}
