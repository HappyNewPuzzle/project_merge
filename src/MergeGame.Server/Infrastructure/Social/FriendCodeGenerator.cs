using System.Security.Cryptography;

namespace MergeGame.Server.Infrastructure.Social;

/// <summary>혼동하기 쉬운 0/O, 1/I 문자를 제외한 암호학적 난수 코드를 만듭니다.</summary>
public sealed class FriendCodeGenerator : IFriendCodeGenerator
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public string Generate()
    {
        Span<char> code = stackalloc char[8];
        for (var index = 0; index < code.Length; index++)
            code[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(code);
    }
}
