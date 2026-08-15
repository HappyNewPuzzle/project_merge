using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>X-Admin-Key 헤더를 고정 시간 해시 비교로 검증하는 별도 인증 스키마입니다.</summary>
public sealed class AdminApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdminApiKey";
    public const string HeaderName = "X-Admin-Key";
    public const string RoleName = "Administrator";
    private readonly AdminApiOptions _adminOptions;

    public AdminApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AdminApiOptions adminOptions) : base(options, logger, encoder)
    {
        _adminOptions = adminOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_adminOptions.Enabled)
            return Task.FromResult(AuthenticateResult.Fail("관리자 API가 비활성화되어 있습니다."));
        if (!Request.Headers.TryGetValue(HeaderName, out var submitted) || submitted.Count != 1)
            return Task.FromResult(AuthenticateResult.Fail("유효한 관리자 API 키가 필요합니다."));

        var credential = ResolveCredential(submitted[0]);
        if (credential is null)
            return Task.FromResult(AuthenticateResult.Fail("유효한 관리자 API 키가 필요합니다."));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, credential.OperatorId),
            new(ClaimTypes.Role, RoleName)
        };
        claims.AddRange(credential.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    private AdminCredentialOptions? ResolveCredential(string? submitted)
    {
        if (_adminOptions.Credentials.Count > 0)
        {
            // 모든 항목을 순회해 키 위치에 따른 조기 종료 시간 차이를 줄이고 마지막 일치 항목만 반환합니다.
            AdminCredentialOptions? matched = null;
            foreach (var credential in _adminOptions.Credentials)
            {
                if (AdminApiKeyValidator.IsValid(submitted, credential.ApiKey))
                    matched = credential;
            }
            return matched;
        }

        return AdminApiKeyValidator.IsValid(submitted, _adminOptions.ApiKey)
            ? new AdminCredentialOptions
            {
                OperatorId = _adminOptions.OperatorId,
                ApiKey = _adminOptions.ApiKey,
                Roles = AdminRoles.All
            }
            : null;
    }
}

/// <summary>키 길이나 다른 위치가 응답 시간에 드러나지 않도록 SHA-256 결과를 비교합니다.</summary>
public static class AdminApiKeyValidator
{
    public static bool IsValid(string? submitted, string configured)
    {
        if (string.IsNullOrEmpty(submitted) || string.IsNullOrEmpty(configured)) return false;
        var submittedHash = SHA256.HashData(Encoding.UTF8.GetBytes(submitted));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(submittedHash, configuredHash);
    }
}
