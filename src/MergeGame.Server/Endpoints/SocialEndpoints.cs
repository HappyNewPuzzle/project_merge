using MergeGame.Server.Application.Social;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>인증 플레이어의 친구 코드, 친구 목록과 일일 에너지 선물 API입니다.</summary>
public static class SocialEndpoints
{
    public static WebApplication MapSocialEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/social").WithTags("Social").RequireAuthorization();
        group.MapPost("/profile", InitializeProfileAsync).WithName("InitializeSocialProfile")
            .Produces<SocialProfileSnapshot>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapGet("/profile", GetProfileAsync).WithName("GetSocialProfile")
            .Produces<SocialState>(StatusCodes.Status200OK).Produces<SocialErrorResponse>(StatusCodes.Status404NotFound);
        group.MapPost("/friends", AddFriendAsync).WithName("AddFriend")
            .Produces<AddFriendResponse>(StatusCodes.Status200OK).Produces<SocialErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<SocialErrorResponse>(StatusCodes.Status422UnprocessableEntity).ProducesValidationProblem();
        group.MapPost("/friends/{friendPlayerId:guid}/energy-gift", SendEnergyGiftAsync).WithName("SendFriendEnergyGift")
            .Produces<EnergyGiftResponse>(StatusCodes.Status200OK).Produces<SocialErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<SocialErrorResponse>(StatusCodes.Status409Conflict).Produces<SocialErrorResponse>(StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static async Task<IResult> InitializeProfileAsync(ICurrentPlayerAccessor accessor, InitializeSocialProfileService service, CancellationToken token)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var profile = await service.ExecuteAsync(playerId, token);
        return profile is null ? Results.NotFound() : Results.Ok(profile);
    }

    private static async Task<IResult> GetProfileAsync(ICurrentPlayerAccessor accessor, GetSocialProfileService service, CancellationToken token)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var state = await service.ExecuteAsync(playerId, token);
        return state is null
            ? Results.NotFound(new SocialErrorResponse("social_profile_not_initialized", "먼저 소셜 프로필을 초기화해야 합니다."))
            : Results.Ok(state);
    }

    private static async Task<IResult> AddFriendAsync(AddFriendRequest request, ICurrentPlayerAccessor accessor, AddFriendService service, CancellationToken token)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var code = request.FriendCode?.Trim().ToUpperInvariant() ?? "";
        if (code.Length != 8 || code.Any(character => !char.IsAsciiLetterOrDigit(character)))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["friendCode"] = ["영문·숫자 8자의 친구 코드가 필요합니다."] });

        var result = await service.ExecuteAsync(playerId, code, token);
        if (result.Status is SocialActionStatus.Succeeded or SocialActionStatus.AlreadyCompleted)
            return Results.Ok(new AddFriendResponse(result.Status == SocialActionStatus.AlreadyCompleted, result.FriendPlayerId!.Value));
        var error = new SocialErrorResponse(result.Error, ToMessage(result.Error));
        return result.Status == SocialActionStatus.NotFound ? Results.NotFound(error) : Results.UnprocessableEntity(error);
    }

    private static async Task<IResult> SendEnergyGiftAsync(Guid friendPlayerId, ICurrentPlayerAccessor accessor, SendFriendEnergyGiftService service, CancellationToken token)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var result = await service.ExecuteAsync(playerId, friendPlayerId, token);
        if (result.Status is SocialActionStatus.Succeeded or SocialActionStatus.AlreadyCompleted)
            return Results.Ok(new EnergyGiftResponse(result.Status == SocialActionStatus.AlreadyCompleted, result.RecipientEconomy));
        var error = new SocialErrorResponse(result.Error, ToMessage(result.Error));
        return result.Status switch
        {
            SocialActionStatus.NotFound => Results.NotFound(error),
            SocialActionStatus.Conflict => Results.Conflict(error),
            _ => Results.UnprocessableEntity(error)
        };
    }

    private static string ToMessage(string error) => error switch
    {
        "friend_code_not_found" => "해당 친구 코드를 찾을 수 없습니다.",
        "cannot_add_self" => "자기 자신은 친구로 추가할 수 없습니다.",
        "friend_not_found" => "친구 관계를 찾을 수 없습니다.",
        "cannot_gift_self" => "자신에게 에너지를 선물할 수 없습니다.",
        "recipient_economy_not_initialized" => "친구의 경제 상태가 아직 초기화되지 않았습니다.",
        "recipient_energy_full" => "친구의 에너지가 이미 최대입니다.",
        "recipient_economy_changed" => "친구의 에너지 상태가 변경됐습니다. 다시 시도해 주세요.",
        _ => "소셜 요청을 처리할 수 없습니다."
    };
}

public sealed record AddFriendRequest(string FriendCode);
public sealed record AddFriendResponse(bool AlreadyFriends, Guid FriendPlayerId);
public sealed record EnergyGiftResponse(bool Replayed, Domain.Economy.EconomySnapshot? RecipientEconomy);
public sealed record SocialErrorResponse(string Code, string Message);
