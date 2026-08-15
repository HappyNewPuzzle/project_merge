using MergeGame.Server.Application.Social;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Domain.Social;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Social;
using Microsoft.EntityFrameworkCore;
using MergeGame.Server.Application.Quests;
using MergeGame.Server.Infrastructure.Quests;

namespace MergeGame.Server.Tests.Application.Social;

/// <summary>친구 추가의 멱등성과 날짜별 에너지 중복 방지를 저장 계층까지 검증합니다.</summary>
public sealed class SocialServicesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddFriend_Twice_CreatesOneNormalizedRelationship()
    {
        await using var db = CreateContext();
        var (first, second) = await SeedPlayersAsync(db);
        db.PlayerSocialProfiles.Add(PlayerSocialProfile.Create(second.Id, "ABCDEFG2", Now.UtcDateTime));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new AddFriendService(db, new StubTimeProvider());

        var created = await service.ExecuteAsync(first.Id, "abcdefg2");
        db.ChangeTracker.Clear();
        var replayed = await service.ExecuteAsync(first.Id, "ABCDEFG2");

        Assert.Equal(SocialActionStatus.Succeeded, created.Status);
        Assert.Equal(SocialActionStatus.AlreadyCompleted, replayed.Status);
        Assert.Single(await db.Friendships.ToListAsync());
    }

    [Fact]
    public async Task SendEnergyGift_Twice_CreditsRecipientOnlyOnce()
    {
        await using var db = CreateContext();
        var (sender, recipient) = await SeedPlayersAsync(db);
        db.Friendships.Add(Friendship.Create(sender.Id, recipient.Id, Now.UtcDateTime));
        var economy = PlayerEconomy.CreateInitial(recipient.Id, Now.UtcDateTime);
        Assert.Equal(EconomyActionError.None, economy.TrySpendGeneratorEnergy(1, Now.UtcDateTime));
        db.PlayerEconomies.Add(economy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new SendFriendEnergyGiftService(
            db,
            new StubTimeProvider(),
            new QuestProgressService(db, new InMemoryQuestCatalog()));

        var sent = await service.ExecuteAsync(sender.Id, recipient.Id);
        db.ChangeTracker.Clear();
        var replayed = await service.ExecuteAsync(sender.Id, recipient.Id);
        var storedEconomy = await db.PlayerEconomies.SingleAsync();

        Assert.Equal(SocialActionStatus.Succeeded, sent.Status);
        Assert.Equal(SocialActionStatus.AlreadyCompleted, replayed.Status);
        Assert.Equal(100, storedEconomy.Energy);
        Assert.Equal(3, storedEconomy.Revision);
        Assert.Single(await db.EnergyGifts.ToListAsync());
        var ledger = Assert.Single(await db.EconomyLedgerEntries.ToListAsync());
        Assert.Equal("friend.energy_received", ledger.Reason);
        Assert.Equal(1, ledger.Delta);
        var quest = await db.PlayerQuests.SingleAsync(value => value.QuestId == "daily_friend_gift_1");
        Assert.True(quest.ToSnapshot().IsCompleted);
    }

    [Fact]
    public async Task InitializeProfile_ForExistingPlayer_IsIdempotent()
    {
        await using var db = CreateContext();
        var (player, _) = await SeedPlayersAsync(db);
        var service = new InitializeSocialProfileService(db, new StubCodeGenerator(), new StubTimeProvider());

        var first = await service.ExecuteAsync(player.Id);
        db.ChangeTracker.Clear();
        var second = await service.ExecuteAsync(player.Id);

        Assert.Equal("SOCIAL22", first!.FriendCode);
        Assert.Equal(first, second);
        Assert.Single(await db.PlayerSocialProfiles.Where(value => value.PlayerId == player.Id).ToListAsync());
    }

    private static MergeGameDbContext CreateContext() => new(new DbContextOptionsBuilder<MergeGameDbContext>()
        .UseInMemoryDatabase($"social-{Guid.NewGuid()}").Options);

    private static async Task<(Player First, Player Second)> SeedPlayersAsync(MergeGameDbContext db)
    {
        var first = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), Now.UtcDateTime);
        var second = Player.CreateGuest(Guid.NewGuid(), new string('B', 64), Now.UtcDateTime);
        db.Players.AddRange(first, second);
        await db.SaveChangesAsync();
        return (first, second);
    }

    private sealed class StubCodeGenerator : IFriendCodeGenerator { public string Generate() => "SOCIAL22"; }
    private sealed class StubTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
