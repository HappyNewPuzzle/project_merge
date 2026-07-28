using MergeGame.Server.Application.Players;

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
            .WithName("CreateGuestPlayer");

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
