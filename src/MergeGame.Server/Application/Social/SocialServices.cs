using MergeGame.Server.Domain.Social;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Social;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Social;

/// <summary>기존 계정에도 소셜 프로필을 지연 생성하여 스키마 도입 전 플레이어를 지원합니다.</summary>
public sealed class InitializeSocialProfileService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IFriendCodeGenerator _codeGenerator;
    private readonly TimeProvider _timeProvider;

    public InitializeSocialProfileService(MergeGameDbContext dbContext, IFriendCodeGenerator codeGenerator, TimeProvider timeProvider)
    {
        _dbContext = dbContext; _codeGenerator = codeGenerator; _timeProvider = timeProvider;
    }

    public async Task<SocialProfileSnapshot?> ExecuteAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.PlayerSocialProfiles.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.PlayerId == playerId, cancellationToken);
        if (existing is not null) return new SocialProfileSnapshot(existing.FriendCode);
        if (!await _dbContext.Players.AnyAsync(player => player.Id == playerId, cancellationToken)) return null;

        // 코드 고유 인덱스 충돌은 매우 드물지만 최대 5회 새 난수로 재시도합니다.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var profile = PlayerSocialProfile.Create(playerId, _codeGenerator.Generate(), _timeProvider.GetUtcNow().UtcDateTime);
            _dbContext.PlayerSocialProfiles.Add(profile);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new SocialProfileSnapshot(profile.FriendCode);
            }
            catch (DbUpdateException)
            {
                _dbContext.ChangeTracker.Clear();
                var concurrent = await _dbContext.PlayerSocialProfiles.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
                if (concurrent is not null) return new SocialProfileSnapshot(concurrent.FriendCode);
            }
        }
        throw new InvalidOperationException("고유한 친구 코드를 생성하지 못했습니다.");
    }
}

/// <summary>친구 코드로 상대를 찾아 양방향 친구 관계를 한 번만 생성합니다.</summary>
public sealed class AddFriendService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    public AddFriendService(MergeGameDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext; _timeProvider = timeProvider;
    }

    public async Task<SocialActionResult> ExecuteAsync(Guid playerId, string friendCode, CancellationToken cancellationToken = default)
    {
        var target = await _dbContext.PlayerSocialProfiles.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.FriendCode == friendCode.ToUpperInvariant(), cancellationToken);
        if (target is null) return new(SocialActionStatus.NotFound, "friend_code_not_found");
        if (target.PlayerId == playerId) return new(SocialActionStatus.InvalidAction, "cannot_add_self");

        var friendship = Friendship.Create(playerId, target.PlayerId, _timeProvider.GetUtcNow().UtcDateTime);
        var exists = await _dbContext.Friendships.AnyAsync(value =>
            value.PlayerLowId == friendship.PlayerLowId && value.PlayerHighId == friendship.PlayerHighId, cancellationToken);
        if (exists) return new(SocialActionStatus.AlreadyCompleted, "already_friends", target.PlayerId);

        _dbContext.Friendships.Add(friendship);
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            // 동시에 서로의 코드를 입력해도 정규화된 고유 인덱스가 한 관계만 남깁니다.
            return new(SocialActionStatus.AlreadyCompleted, "already_friends", target.PlayerId);
        }
        return new(SocialActionStatus.Succeeded, "none", target.PlayerId);
    }
}

/// <summary>현재 친구 목록과 오늘 선물 발송 여부를 한 번에 조회합니다.</summary>
public sealed class GetSocialProfileService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    public GetSocialProfileService(MergeGameDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext; _timeProvider = timeProvider;
    }

    public async Task<SocialState?> ExecuteAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.PlayerSocialProfiles.AsNoTracking()
            .SingleOrDefaultAsync(value => value.PlayerId == playerId, cancellationToken);
        if (profile is null) return null;

        var relations = await _dbContext.Friendships.AsNoTracking()
            .Where(value => value.PlayerLowId == playerId || value.PlayerHighId == playerId)
            .ToListAsync(cancellationToken);
        var friendIds = relations.Select(value => value.GetOtherPlayerId(playerId)).ToArray();
        var players = await _dbContext.Players.AsNoTracking().Where(value => friendIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var giftedIds = await _dbContext.EnergyGifts.AsNoTracking()
            .Where(value => value.SenderPlayerId == playerId && value.GiftDateUtc == today)
            .Select(value => value.RecipientPlayerId).ToListAsync(cancellationToken);

        var friends = relations.Select(relation =>
        {
            var friendId = relation.GetOtherPlayerId(playerId);
            return new FriendSnapshot(friendId, players[friendId].DisplayName, relation.CreatedAtUtc, giftedIds.Contains(friendId));
        }).OrderBy(value => value.DisplayName).ToArray();
        return new SocialState(profile.FriendCode, friends);
    }
}

/// <summary>친구에게 UTC 날짜별 한 번 에너지 5를 즉시 지급합니다.</summary>
public sealed class SendFriendEnergyGiftService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    public SendFriendEnergyGiftService(MergeGameDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext; _timeProvider = timeProvider;
    }

    public async Task<EnergyGiftResult> ExecuteAsync(Guid senderId, Guid recipientId, CancellationToken cancellationToken = default)
    {
        if (senderId == recipientId) return new(SocialActionStatus.InvalidAction, "cannot_gift_self", null);
        var isFriend = await _dbContext.Friendships.AsNoTracking().AnyAsync(value =>
            (value.PlayerLowId == senderId && value.PlayerHighId == recipientId)
            || (value.PlayerLowId == recipientId && value.PlayerHighId == senderId), cancellationToken);
        if (!isFriend) return new(SocialActionStatus.NotFound, "friend_not_found", null);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (await _dbContext.EnergyGifts.AnyAsync(value => value.SenderPlayerId == senderId
            && value.RecipientPlayerId == recipientId && value.GiftDateUtc == now.Date, cancellationToken))
            return new(SocialActionStatus.AlreadyCompleted, "gift_already_sent_today", null);

        var economy = await _dbContext.PlayerEconomies.SingleOrDefaultAsync(value => value.PlayerId == recipientId, cancellationToken);
        if (economy is null) return new(SocialActionStatus.NotFound, "recipient_economy_not_initialized", null);
        var persistedEnergyBefore = economy.Energy;
        var projectedEnergyBefore = economy.CreateSnapshot(now).Energy;
        if (economy.TryReceiveFriendEnergy(now) != Domain.Economy.EconomyActionError.None)
            return new(SocialActionStatus.InvalidAction, "recipient_energy_full", economy.CreateSnapshot(now));

        _dbContext.EnergyGifts.Add(EnergyGift.Create(senderId, recipientId, now));
        if (projectedEnergyBefore > persistedEnergyBefore)
        {
            _dbContext.EconomyLedgerEntries.Add(Domain.Economy.EconomyLedgerEntry.CreateEnergy(
                recipientId,
                "energy.recharged",
                projectedEnergyBefore - persistedEnergyBefore,
                projectedEnergyBefore,
                economy.Revision,
                $"friend-gift:{senderId:N}:{now:yyyy-MM-dd}:recharge",
                now));
        }
        _dbContext.EconomyLedgerEntries.Add(Domain.Economy.EconomyLedgerEntry.CreateEnergy(
            recipientId,
            "friend.energy_received",
            economy.Energy - projectedEnergyBefore,
            economy.Energy,
            economy.Revision,
            $"friend-gift:{senderId:N}:{now:yyyy-MM-dd}",
            now));
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return new(SocialActionStatus.Conflict, "recipient_economy_changed", null); }
        catch (DbUpdateException) { return new(SocialActionStatus.AlreadyCompleted, "gift_already_sent_today", null); }
        return new(SocialActionStatus.Succeeded, "none", economy.CreateSnapshot(now));
    }
}

public enum SocialActionStatus { Succeeded, AlreadyCompleted, NotFound, InvalidAction, Conflict }
public sealed record SocialProfileSnapshot(string FriendCode);
public sealed record FriendSnapshot(Guid PlayerId, string DisplayName, DateTime FriendsSinceUtc, bool EnergyGiftSentToday);
public sealed record SocialState(string FriendCode, IReadOnlyList<FriendSnapshot> Friends);
public sealed record SocialActionResult(SocialActionStatus Status, string Error, Guid? FriendPlayerId = null);
public sealed record EnergyGiftResult(SocialActionStatus Status, string Error, Domain.Economy.EconomySnapshot? RecipientEconomy);
