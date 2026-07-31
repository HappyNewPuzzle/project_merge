namespace MergeGame.Server.Domain.Social;

/// <summary>
/// 친구 찾기에 사용하는 공개 프로필입니다. 인증용 플레이어 ID를 직접 공유하지 않고
/// 짧은 무작위 친구 코드만 노출해 계정 식별자 열거를 어렵게 합니다.
/// </summary>
public sealed class PlayerSocialProfile
{
    private PlayerSocialProfile() { }

    public Guid PlayerId { get; private set; }
    public string FriendCode { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static PlayerSocialProfile Create(Guid playerId, string friendCode, DateTime createdAtUtc)
    {
        if (playerId == Guid.Empty) throw new ArgumentException("플레이어 ID가 필요합니다.", nameof(playerId));
        if (friendCode.Length != 8 || friendCode.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("친구 코드는 영문·숫자 8자여야 합니다.", nameof(friendCode));

        return new PlayerSocialProfile
        {
            PlayerId = playerId,
            FriendCode = friendCode.ToUpperInvariant(),
            CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)
        };
    }
}
