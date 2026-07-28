using System.Security.Cryptography;
using System.Text;
using MergeGame.Server.Infrastructure.Security;

namespace MergeGame.Server.Tests.Infrastructure.Security;

/// <summary>
/// 운영용 게스트 토큰 생성기의 길이, 문자 형식, 해시 계산을 검증합니다.
/// </summary>
public sealed class GuestCredentialGeneratorTests
{
    /// <summary>
    /// 생성된 토큰이 URL-safe Base64 형식이고 DB 저장용 SHA-256 해시와 일치하는지 확인합니다.
    /// </summary>
    [Fact]
    public void Generate_ReturnsUrlSafeTokenAndMatchingSha256Hash()
    {
        var generator = new GuestCredentialGenerator();

        var credential = generator.Generate();

        // 32바이트를 패딩 없는 Base64로 표현하면 43자가 됩니다.
        Assert.Equal(43, credential.RawToken.Length);
        Assert.DoesNotContain("+", credential.RawToken);
        Assert.DoesNotContain("/", credential.RawToken);
        Assert.DoesNotContain("=", credential.RawToken);

        var expectedHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(credential.RawToken)));

        Assert.Equal(expectedHash, credential.TokenHash);
        Assert.Equal(64, credential.TokenHash.Length);
    }

    /// <summary>
    /// 두 번 생성한 토큰이 재사용되지 않는지 확인합니다.
    /// </summary>
    [Fact]
    public void Generate_CalledTwice_ReturnsDifferentTokens()
    {
        var generator = new GuestCredentialGenerator();

        var first = generator.Generate();
        var second = generator.Generate();

        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.NotEqual(first.TokenHash, second.TokenHash);
    }
}
