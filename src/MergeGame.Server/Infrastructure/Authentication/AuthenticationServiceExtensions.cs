using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>
/// JWT 인증, 현재 플레이어 컨텍스트, 인증 API 속도 제한을 등록합니다.
/// </summary>
public static class AuthenticationServiceExtensions
{
    private const string JwtSectionName = "Jwt";

    /// <summary>
    /// 플레이어 인증에 필요한 모든 서비스를 등록하고 JWT 설정을 시작 전에 검증합니다.
    /// </summary>
    /// <param name="services">애플리케이션 DI 서비스 컬렉션입니다.</param>
    /// <param name="configuration">환경 변수가 합쳐진 서버 설정입니다.</param>
    /// <returns>추가 서비스 등록을 이어갈 수 있도록 서비스 컬렉션을 반환합니다.</returns>
    public static IServiceCollection AddPlayerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration
            .GetRequiredSection(JwtSectionName)
            .Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt 설정 섹션이 필요합니다.");

        ValidateJwtOptions(jwtOptions);
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        // 발급기와 검증기가 정확히 같은 설정 객체를 사용하도록 단일 인스턴스로 등록합니다.
        services.AddSingleton(jwtOptions);
        services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // sub 클레임 이름이 프레임워크의 긴 URI 클레임으로 자동 변환되지 않도록 유지합니다.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    // 분산 서버 간 작은 시각 오차만 허용하고 만료 토큰의 추가 사용 시간을 최소화합니다.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentPlayerAccessor, CurrentPlayerAccessor>();

        services.AddRateLimiter(options =>
        {
            // 제한 초과를 일반 서버 오류와 구별할 수 있도록 HTTP 429로 응답합니다.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthenticationRateLimitPolicy.Name, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    /// <summary>
    /// 안전하지 않은 기본값이나 잘못된 수명 설정으로 서버가 실행되는 것을 차단합니다.
    /// </summary>
    private static void ValidateJwtOptions(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException("Jwt:Issuer 설정이 필요합니다.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("Jwt:Audience 설정이 필요합니다.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey)
            || Encoding.UTF8.GetByteCount(options.SigningKey) < 32
            || options.SigningKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || options.SigningKey.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey에는 예시 값이 아닌 최소 32바이트 비밀 키가 필요합니다.");
        }

        if (options.AccessTokenMinutes is < 1 or > 60)
        {
            throw new InvalidOperationException(
                "Jwt:AccessTokenMinutes는 1분 이상 60분 이하여야 합니다.");
        }
    }
}

/// <summary>
/// 인증과 계정 생성처럼 남용될 수 있는 엔드포인트가 공유하는 속도 제한 정책 이름입니다.
/// </summary>
public static class AuthenticationRateLimitPolicy
{
    /// <summary>
    /// 엔드포인트와 서비스 등록이 함께 사용하는 정책 식별자입니다.
    /// </summary>
    public const string Name = "authentication-actions";
}
