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
}
