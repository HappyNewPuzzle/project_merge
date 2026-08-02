using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Tests.Infrastructure.Authentication;

/// <summary>관리자 키가 정확히 일치할 때만 인증되는지 검증합니다.</summary>
public sealed class AdminApiKeyValidatorTests
{
    [Fact]
    public void IsValid_AcceptsExactKeyAndRejectsMissingOrDifferentKey()
    {
        const string configured = "admin-key-with-at-least-thirty-two-bytes";
        Assert.True(AdminApiKeyValidator.IsValid(configured, configured));
        Assert.False(AdminApiKeyValidator.IsValid("different-key", configured));
        Assert.False(AdminApiKeyValidator.IsValid(null, configured));
    }
}
