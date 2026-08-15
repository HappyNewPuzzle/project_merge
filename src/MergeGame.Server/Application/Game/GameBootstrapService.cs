using MergeGame.Server.Application.Boards;
using MergeGame.Server.Application.Players;
using MergeGame.Server.Application.Social;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Content;
using MergeGame.Server.Domain.Generators;
using MergeGame.Server.Domain.Quests;
using MergeGame.Server.Domain.Social;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Social;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Game;

/// <summary>
/// 로그인 직후 필요한 플레이어 하위 상태를 누락된 경우 한 트랜잭션으로 생성하고,
/// Unity가 즉시 플레이 화면을 구성할 수 있는 일관된 전체 스냅샷을 반환합니다.
/// </summary>
public sealed class GameBootstrapService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IItemCatalog _itemCatalog;
    private readonly IGeneratorCatalog _generatorCatalog;
    private readonly IFriendCodeGenerator _friendCodeGenerator;
    private readonly IQuestCatalog _questCatalog;
    private readonly TimeProvider _timeProvider;

    public GameBootstrapService(
        MergeGameDbContext dbContext,
        IItemCatalog itemCatalog,
        IGeneratorCatalog generatorCatalog,
        IFriendCodeGenerator friendCodeGenerator,
        IQuestCatalog questCatalog,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _itemCatalog = itemCatalog;
        _generatorCatalog = generatorCatalog;
        _friendCodeGenerator = friendCodeGenerator;
        _questCatalog = questCatalog;
        _timeProvider = timeProvider;
    }

    public async Task<GameBootstrapResponse?> ExecuteAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Players.AsNoTracking().AnyAsync(
                value => value.Id == playerId, cancellationToken))
            return null;

        // 동시 로그인 초기화나 극히 드문 친구 코드 충돌은 전체 트랜잭션을 새 상태로 최대 5회 재시도합니다.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await TryExecuteOnceAsync(playerId, cancellationToken);
            if (response is not null)
                return response;
        }

        // 재시도 도중 계정이 삭제된 경우에는 인증 주체가 더 이상 존재하지 않는 정상 404로 처리합니다.
        if (!await _dbContext.Players.AsNoTracking().AnyAsync(
                value => value.Id == playerId, cancellationToken))
            return null;
        throw new InvalidOperationException("게임 초기 상태를 동시성 충돌 없이 생성하지 못했습니다.");
    }

    private async Task<GameBootstrapResponse?> TryExecuteOnceAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var player = await _dbContext.Players.SingleOrDefaultAsync(
            value => value.Id == playerId, cancellationToken);
        if (player is null)
            return null;

        var board = await _dbContext.PlayerBoards.Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        if (board is null)
        {
            board = PlayerBoard.CreateInitial(playerId, now);
            _dbContext.PlayerBoards.Add(board);
        }

        var economy = await _dbContext.PlayerEconomies.SingleOrDefaultAsync(
            value => value.PlayerId == playerId, cancellationToken);
        if (economy is null)
        {
            economy = PlayerEconomy.CreateInitial(playerId, now);
            _dbContext.PlayerEconomies.Add(economy);
        }

        var quests = await _dbContext.PlayerQuests
            .Where(value => value.PlayerId == playerId)
            .ToListAsync(cancellationToken);
        foreach (var definition in _questCatalog.GetAll())
        {
            var periodKey = QuestPeriodKey.Create(definition.PeriodType, now);
            var quest = quests.SingleOrDefault(value => value.QuestId == definition.QuestId);
            if (quest is null)
            {
                quest = PlayerQuest.Create(playerId, definition, periodKey);
                quests.Add(quest);
                _dbContext.PlayerQuests.Add(quest);
            }
            else
            {
                quest.EnsureCurrentPeriod(definition, periodKey);
            }
        }

        var socialProfile = await _dbContext.PlayerSocialProfiles.SingleOrDefaultAsync(
            value => value.PlayerId == playerId, cancellationToken);
        if (socialProfile is null)
        {
            socialProfile = PlayerSocialProfile.Create(playerId, _friendCodeGenerator.Generate(), now);
            _dbContext.PlayerSocialProfiles.Add(socialProfile);
        }

        var existingGenerators = await _dbContext.PlayerGenerators
            .Where(value => value.PlayerId == playerId)
            .ToDictionaryAsync(value => value.GeneratorId, StringComparer.Ordinal, cancellationToken);
        foreach (var definition in _generatorCatalog.GetAll())
        {
            if (existingGenerators.ContainsKey(definition.Id))
                continue;
            var generator = PlayerGenerator.CreateInitial(playerId, definition, now);
            existingGenerators.Add(definition.Id, generator);
            _dbContext.PlayerGenerators.Add(generator);
        }

        try
        {
            // 모든 누락 상태는 한 SaveChanges 트랜잭션으로 생성되어 부분 초기화가 남지 않습니다.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            return null;
        }

        var social = await BuildSocialStateAsync(playerId, socialProfile.FriendCode, now, cancellationToken);
        var generatorStates = _generatorCatalog.GetAll()
            .Select(definition => existingGenerators[definition.Id].CreateSnapshot(now, definition))
            .ToArray();

        return new GameBootstrapResponse(
            DateTime.SpecifyKind(now, DateTimeKind.Utc),
            GameContentVersion.Current,
            new PlayerProfile(player.Id, player.DisplayName, player.CreatedAtUtc),
            BoardStateMapper.Map(board, _itemCatalog),
            economy.CreateSnapshot(now),
            generatorStates,
            quests.OrderBy(value => value.QuestId, StringComparer.Ordinal)
                .Select(value => value.ToSnapshot()).ToArray(),
            social);
    }

    private async Task<SocialState> BuildSocialStateAsync(
        Guid playerId,
        string friendCode,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var relations = await _dbContext.Friendships.AsNoTracking()
            .Where(value => value.PlayerLowId == playerId || value.PlayerHighId == playerId)
            .ToListAsync(cancellationToken);
        var friendIds = relations.Select(value => value.GetOtherPlayerId(playerId)).ToArray();
        var players = await _dbContext.Players.AsNoTracking()
            .Where(value => friendIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var giftedIds = await _dbContext.EnergyGifts.AsNoTracking()
            .Where(value => value.SenderPlayerId == playerId && value.GiftDateUtc == nowUtc.Date)
            .Select(value => value.RecipientPlayerId)
            .ToListAsync(cancellationToken);

        var friends = relations.Select(relation =>
        {
            var friendId = relation.GetOtherPlayerId(playerId);
            return new FriendSnapshot(
                friendId,
                players[friendId].DisplayName,
                relation.CreatedAtUtc,
                giftedIds.Contains(friendId));
        }).OrderBy(value => value.DisplayName).ToArray();
        return new SocialState(friendCode, friends);
    }
}

/// <summary>Unity가 로그인 직후 로컬 게임 상태를 한 번에 교체하는 전체 스냅샷입니다.</summary>
public sealed record GameBootstrapResponse(
    DateTime ServerTimeUtc,
    string ContentVersion,
    PlayerProfile Player,
    BoardState Board,
    EconomySnapshot Economy,
    IReadOnlyList<GeneratorState> Generators,
    IReadOnlyList<QuestSnapshot> Quests,
    SocialState Social);
