using MergeGame.Server.Application.Players;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>
/// 플레이어 계정과 관련된 HTTP 엔드포인트를 등록합니다.
/// </summary>
public static class PlayerEndpoints
{
    /// <summary>
    /// 버전 1 플레이어 API를 등록합니다.
    /// </summary>
    /// <param name="app">라우트를 등록할 ASP.NET Core 애플리케이션입니다.</param>
    /// <returns>추가 매핑을 이어갈 수 있도록 입력 애플리케이션을 반환합니다.</returns>
    public static WebApplication MapPlayerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/players")
            .WithTags("Players");

        group.MapPost("/guest", CreateGuestAsync)
            .WithName("CreateGuestPlayer")
            .Produces<CreateGuestPlayerResponse>(StatusCodes.Status201Created)
            .RequireRateLimiting(AuthenticationRateLimitPolicy.Name);

        group.MapGet("/me", GetCurrentPlayerAsync)
            .WithName("GetCurrentPlayer")
            .Produces<CurrentPlayerResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        return app;
    }

    /// <summary>
    /// 요청 본문 없이 새 게스트 계정을 생성합니다.
    /// </summary>
    /// <remarks>
    /// 응답의 guestToken은 서버가 다시 보여 주지 않으므로 클라이언트의 보안 저장소에 보관해야 합니다.
    /// </remarks>
    private static async Task<IResult> CreateGuestAsync(
        CreateGuestPlayerService service,
        CancellationToken cancellationToken)
    {
        var createdPlayer = await service.ExecuteAsync(cancellationToken);
        var response = new CreateGuestPlayerResponse(
            createdPlayer.PlayerId,
            createdPlayer.DisplayName,
            createdPlayer.GuestToken,
            createdPlayer.CreatedAtUtc);

        // Location은 이후 플레이어 조회 API가 사용할 표준 리소스 주소를 미리 명시합니다.
        return Results.Created($"/api/v1/players/{createdPlayer.PlayerId}", response);
    }

    /// <summary>
    /// 검증된 JWT의 sub 클레임에 해당하는 현재 플레이어 프로필을 반환합니다.
    /// </summary>
    private static async Task<IResult> GetCurrentPlayerAsync(
        ICurrentPlayerAccessor currentPlayer,
        GetPlayerProfileService service,
        CancellationToken cancellationToken)
    {
        if (!currentPlayer.TryGetPlayerId(out var playerId))
        {
            // 정상 JWT라면 발생하지 않지만 필수 sub 클레임이 없거나 잘못된 경우 접근을 거부합니다.
            return Results.Unauthorized();
        }

        var profile = await service.ExecuteAsync(playerId, cancellationToken);
        if (profile is null)
        {
            // 토큰 발급 이후 계정이 삭제된 경우 더 이상 존재하지 않는 리소스로 처리합니다.
            return Results.NotFound();
        }

        return Results.Ok(new CurrentPlayerResponse(
            profile.PlayerId,
            profile.DisplayName,
            profile.CreatedAtUtc));
    }
}

/// <summary>
/// 게스트 계정 생성 성공 응답입니다.
/// </summary>
/// <param name="PlayerId">서버가 발급한 플레이어 식별자입니다.</param>
/// <param name="DisplayName">자동 생성된 기본 표시 이름입니다.</param>
/// <param name="GuestToken">이후 로그인에 사용할 일회성 표시 원본 토큰입니다.</param>
/// <param name="CreatedAtUtc">계정이 생성된 UTC 시각입니다.</param>
public sealed record CreateGuestPlayerResponse(
    Guid PlayerId,
    string DisplayName,
    string GuestToken,
    DateTime CreatedAtUtc);

/// <summary>
/// 인증된 현재 플레이어의 공개 프로필 응답입니다.
/// </summary>
/// <param name="PlayerId">JWT와 일치하는 플레이어 식별자입니다.</param>
/// <param name="DisplayName">현재 게임 표시 이름입니다.</param>
/// <param name="CreatedAtUtc">계정 생성 UTC 시각입니다.</param>
public sealed record CurrentPlayerResponse(
    Guid PlayerId,
    string DisplayName,
    DateTime CreatedAtUtc);
