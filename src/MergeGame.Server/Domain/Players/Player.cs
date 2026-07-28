namespace MergeGame.Server.Domain.Players;

/// <summary>
/// 머지 게임을 이용하는 한 명의 플레이어를 나타내는 도메인 엔티티입니다.
/// 현재 단계에서는 게스트 계정에 필요한 최소 정보만 보유하고 이후 보드와 재화가 연결됩니다.
/// </summary>
public sealed class Player
{
    // EF Core가 데이터베이스 값을 채워 객체를 복원할 때 사용하는 생성자입니다.
    // 애플리케이션 코드가 불완전한 Player를 직접 만들지 못하도록 private으로 제한합니다.
    private Player()
    {
    }

    /// <summary>
    /// 서버가 발급하는 플레이어의 전역 고유 식별자입니다.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// 화면에 표시할 기본 게스트 이름입니다.
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>
    /// 게스트 접근 토큰을 SHA-256으로 해시한 64자리 16진수 문자열입니다.
    /// 토큰 원문은 생성 응답에서 한 번만 전달하며 데이터베이스에는 저장하지 않습니다.
    /// </summary>
    public string GuestTokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// 계정이 만들어진 UTC 시각입니다.
    /// UTC로 통일하면 서버나 플레이어의 시간대가 달라도 일관되게 비교할 수 있습니다.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// 유효한 게스트 플레이어를 생성하는 유일한 진입점입니다.
    /// </summary>
    /// <param name="id">서버에서 생성한 플레이어 식별자입니다.</param>
    /// <param name="guestTokenHash">원본 토큰을 SHA-256으로 해시한 값입니다.</param>
    /// <param name="createdAtUtc">계정 생성 UTC 시각입니다.</param>
    /// <returns>필수 값이 모두 설정된 새 플레이어입니다.</returns>
    /// <exception cref="ArgumentException">토큰 해시가 SHA-256 16진수 길이와 다를 때 발생합니다.</exception>
    public static Player CreateGuest(
        Guid id,
        string guestTokenHash,
        DateTime createdAtUtc)
    {
        if (guestTokenHash.Length != 64)
        {
            throw new ArgumentException(
                "게스트 토큰 해시는 64자리 SHA-256 16진수 문자열이어야 합니다.",
                nameof(guestTokenHash));
        }

        // 식별자의 앞 8자만 사용해 읽기 쉬운 기본 이름을 만듭니다.
        // 사용자가 이름을 변경하는 기능은 별도 단계에서 금칙어와 길이 검증을 함께 구현합니다.
        var shortId = id.ToString("N")[..8];

        return new Player
        {
            Id = id,
            DisplayName = $"Guest-{shortId}",
            GuestTokenHash = guestTokenHash,
            CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)
        };
    }
}
