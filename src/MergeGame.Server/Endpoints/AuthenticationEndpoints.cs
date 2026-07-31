using MergeGame.Server.Application.Authentication;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>
/// 플레이어 로그인과 액세스 토큰 발급 엔드포인트를 등록합니다.
/// </summary>
public static class AuthenticationEndpoints
{
    /// <summary>
    /// 버전 1 인증 API를 등록합니다.
    /// </summary>
    public static WebApplication MapAuthenticationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        group.MapPost("/guest", LoginGuestAsync)
            .WithName("LoginGuest")
            .Produces<GuestLoginResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(AuthenticationRateLimitPolicy.Name);

        return app;
    }

    /// <summary>
    /// 게스트 자격 증명을 검증하고 Bearer JWT를 반환합니다.
    /// </summary>
    private static async Task<IResult> LoginGuestAsync(
        GuestLoginRequest request,
        AuthenticateGuestPlayerService service,
        CancellationToken cancellationToken)
    {
        if (request.PlayerId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.GuestToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["playerId와 guestToken이 필요합니다."]
            });
        }

        var result = await service.ExecuteAsync(
            request.PlayerId,
            request.GuestToken,
            cancellationToken);

        if (result is null)
        {
            // 플레이어 존재 여부와 토큰 오류를 구분하지 않아 계정 식별자 탐색을 방지합니다.
            return Results.Unauthorized();
        }

        return Results.Ok(new GuestLoginResponse(
            result.PlayerId,
            result.AccessToken,
            TokenType: "Bearer",
            result.ExpiresAtUtc));
    }
}

/// <summary>
/// 게스트 로그인 요청입니다.
/// </summary>
/// <param name="PlayerId">게스트 생성 응답에서 받은 플레이어 ID입니다.</param>
/// <param name="GuestToken">클라이언트 보안 저장소에 보관한 원본 게스트 토큰입니다.</param>
public sealed record GuestLoginRequest(Guid PlayerId, string GuestToken);

/// <summary>
/// 성공한 게스트 로그인 응답입니다.
/// </summary>
/// <param name="PlayerId">인증된 플레이어 ID입니다.</param>
/// <param name="AccessToken">보호 API 요청에 사용할 JWT입니다.</param>
/// <param name="TokenType">Authorization 헤더에 사용할 Bearer 형식입니다.</param>
/// <param name="ExpiresAtUtc">액세스 토큰 만료 UTC 시각입니다.</param>
public sealed record GuestLoginResponse(
    Guid PlayerId,
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc);
