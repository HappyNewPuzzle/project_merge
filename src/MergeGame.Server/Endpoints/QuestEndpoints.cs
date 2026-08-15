using MergeGame.Server.Application.Quests;
using MergeGame.Server.Infrastructure.Authentication;

namespace MergeGame.Server.Endpoints;

/// <summary>인증 플레이어의 퀘스트 조회와 멱등 보상 수령 API입니다.</summary>
public static class QuestEndpoints
{
    public static WebApplication MapQuestEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/quests").WithTags("Quests").RequireAuthorization();
        group.MapPost("/", InitializeAsync).WithName("InitializeQuests")
            .Produces<IReadOnlyList<Domain.Quests.QuestSnapshot>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/", GetAsync).WithName("GetQuests")
            .Produces<IReadOnlyList<Domain.Quests.QuestSnapshot>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{questId}/claim", ClaimAsync).WithName("ClaimQuestReward")
            .Produces<QuestRewardResponse>(StatusCodes.Status200OK)
            .Produces<QuestRewardResponse>(StatusCodes.Status404NotFound)
            .Produces<QuestRewardResponse>(StatusCodes.Status409Conflict)
            .Produces<QuestRewardResponse>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem();
        return app;
    }

    private static async Task<IResult> InitializeAsync(
        ICurrentPlayerAccessor accessor,
        QuestQueryService service,
        CancellationToken cancellationToken)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var quest = await service.InitializeAsync(playerId, cancellationToken);
        return quest is null ? Results.NotFound() : Results.Ok(quest);
    }

    private static async Task<IResult> GetAsync(
        ICurrentPlayerAccessor accessor,
        QuestQueryService service,
        CancellationToken cancellationToken)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        var quest = await service.GetAsync(playerId, cancellationToken);
        return quest is null ? Results.NotFound() : Results.Ok(quest);
    }

    private static async Task<IResult> ClaimAsync(
        string questId,
        ClaimQuestRewardRequest request,
        ICurrentPlayerAccessor accessor,
        ClaimQuestRewardService service,
        CancellationToken cancellationToken)
    {
        if (!accessor.TryGetPlayerId(out var playerId)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 64)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idempotencyKey"] = ["1~64자의 멱등성 키가 필요합니다."]
            });
        }

        var result = await service.ExecuteAsync(
            playerId,
            questId,
            request.IdempotencyKey,
            request.ExpectedQuestRevision,
            request.ExpectedEconomyRevision,
            cancellationToken);
        var response = new QuestRewardResponse(
            result.Status == QuestRewardStatus.Replayed,
            result.Quest,
            result.Economy,
            result.Error.ToString());
        return result.Status switch
        {
            QuestRewardStatus.Succeeded or QuestRewardStatus.Replayed => Results.Ok(response),
            QuestRewardStatus.NotFound => Results.NotFound(response),
            QuestRewardStatus.Conflict => Results.Conflict(response),
            _ => Results.UnprocessableEntity(response)
        };
    }
}

public sealed record ClaimQuestRewardRequest(
    string IdempotencyKey,
    long ExpectedQuestRevision,
    long ExpectedEconomyRevision);

public sealed record QuestRewardResponse(
    bool Replayed,
    Domain.Quests.QuestSnapshot? Quest,
    Domain.Economy.EconomySnapshot? Economy,
    string Error);
