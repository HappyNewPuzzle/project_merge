namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>플레이어 JWT와 완전히 분리된 운영자 API 인증 설정입니다.</summary>
public sealed class AdminApiOptions
{
    public const string SectionName = "AdminApi";
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string OperatorId { get; init; } = "operations";
}
