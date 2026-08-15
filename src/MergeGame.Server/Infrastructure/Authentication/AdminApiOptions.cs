namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>플레이어 JWT와 완전히 분리된 운영자 API 인증 설정입니다.</summary>
public sealed class AdminApiOptions
{
    public const string SectionName = "AdminApi";
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string OperatorId { get; init; } = "operations";
    /// <summary>한 요청에서 증가하거나 차감할 수 있는 코인의 절대값 상한입니다.</summary>
    public long MaxAbsoluteCoinAdjustment { get; init; } = 10_000;
    public long RequireTwoPersonApprovalAtOrAbove { get; init; } = 5_000;
    /// <summary>운영자별 키와 최소 권한 역할 목록입니다. 비어 있으면 기존 단일 키 설정을 사용합니다.</summary>
    public IReadOnlyList<AdminCredentialOptions> Credentials { get; init; } = [];
}

public sealed class AdminCredentialOptions
{
    public string OperatorId { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public static class AdminRoles
{
    public const string Reader = "AdminReader";
    public const string Moderator = "AdminModerator";
    public const string Economy = "AdminEconomy";
    public static readonly IReadOnlyList<string> All = [Reader, Moderator, Economy];
}
