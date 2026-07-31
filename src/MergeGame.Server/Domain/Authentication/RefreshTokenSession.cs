namespace MergeGame.Server.Domain.Authentication;

/// <summary>DB에는 원문 대신 SHA-256 해시만 저장하는 회전형 로그인 세션입니다.</summary>
public sealed class RefreshTokenSession
{
    private RefreshTokenSession() { }
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? ReplacedBySessionId { get; private set; }
    public string? RevocationReason { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    public static RefreshTokenSession Create(Guid playerId, Guid familyId, string hash, DateTime nowUtc, DateTime expiresAtUtc)
    {
        if (hash.Length != 64) throw new ArgumentException("토큰 해시는 SHA-256 형식이어야 합니다.", nameof(hash));
        return new RefreshTokenSession { Id = Guid.NewGuid(), PlayerId = playerId, FamilyId = familyId,
            TokenHash = hash, CreatedAtUtc = nowUtc, ExpiresAtUtc = expiresAtUtc };
    }

    /// <summary>회전 또는 로그아웃된 토큰을 다시 사용할 수 없게 표시합니다.</summary>
    public void Revoke(DateTime nowUtc, string reason, Guid? replacementId = null)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = nowUtc; RevocationReason = reason; ReplacedBySessionId = replacementId;
    }
}
