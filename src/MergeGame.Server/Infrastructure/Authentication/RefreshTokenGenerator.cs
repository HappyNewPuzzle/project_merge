using System.Security.Cryptography;
using MergeGame.Server.Infrastructure.Security;

namespace MergeGame.Server.Infrastructure.Authentication;

public interface IRefreshTokenGenerator { GeneratedRefreshToken Generate(); }
public sealed record GeneratedRefreshToken(string RawToken, string TokenHash);

/// <summary>256비트 난수 원문과 DB 저장용 SHA-256 해시를 함께 생성합니다.</summary>
public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public GeneratedRefreshToken Generate()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new GeneratedRefreshToken(raw, GuestTokenHasher.Hash(raw));
    }
}
