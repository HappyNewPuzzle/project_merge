using MergeGame.Server.Application.Game;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Domain.Content;
using MergeGame.Server.Infrastructure.Generators;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Social;
using MergeGame.Server.Infrastructure.Quests;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Game;

/// <summary>게임 진입 시 모든 필수 상태가 한 번만 만들어지고 한 응답으로 반환되는지 검증합니다.</summary>
public sealed class GameBootstrapServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_NewPlayer_CreatesCompletePlayableState()
    {
        await using var fixture = await Fixture.CreateAsync(withPlayer: true);

        var result = await fixture.Service.ExecuteAsync(fixture.PlayerId);

        Assert.NotNull(result);
        Assert.Equal(GameContentVersion.Current, result.ContentVersion);
        Assert.Equal(Now.UtcDateTime, result.ServerTimeUtc);
        Assert.Equal(2, result.Board.Items.Count);
        Assert.Equal(100, result.Economy.Energy);
        Assert.Equal(20, result.Inventory.Capacity);
        Assert.Empty(result.Inventory.Items);
        Assert.Single(result.Generators);
        Assert.Equal(5, result.Generators[0].Charges);
        Assert.Equal(5, result.Quests.Count);
        Assert.Equal("ABCDEFGH", result.Social.FriendCode);
        Assert.Equal(1, await fixture.Db.PlayerBoards.CountAsync());
        Assert.Equal(1, await fixture.Db.PlayerEconomies.CountAsync());
        Assert.Equal(5, await fixture.Db.PlayerQuests.CountAsync());
        Assert.Equal(1, await fixture.Db.PlayerSocialProfiles.CountAsync());
        Assert.Equal(1, await fixture.Db.PlayerGenerators.CountAsync());
        Assert.Equal(1, await fixture.Db.PlayerInventories.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_RepeatedCall_DoesNotDuplicateInitializedState()
    {
        await using var fixture = await Fixture.CreateAsync(withPlayer: true);
        var first = await fixture.Service.ExecuteAsync(fixture.PlayerId);
        fixture.Db.ChangeTracker.Clear();

        var second = await fixture.Service.ExecuteAsync(fixture.PlayerId);

        Assert.Equal(first!.Board.Revision, second!.Board.Revision);
        Assert.Equal(first.Economy.Revision, second.Economy.Revision);
        Assert.Equal(first.Social.FriendCode, second.Social.FriendCode);
        Assert.Equal(2, await fixture.Db.BoardItems.CountAsync());
        Assert.Equal(1, await fixture.Db.PlayerGenerators.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_DeletedAuthenticatedPlayer_ReturnsNull()
    {
        await using var fixture = await Fixture.CreateAsync(withPlayer: false);

        var result = await fixture.Service.ExecuteAsync(fixture.PlayerId);

        Assert.Null(result);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(MergeGameDbContext db, Guid playerId)
        {
            Db = db;
            PlayerId = playerId;
            Service = new GameBootstrapService(
                db,
                new InMemoryItemCatalog(),
                new InMemoryGeneratorCatalog(),
                new StubFriendCodeGenerator(),
                new InMemoryQuestCatalog(),
                new StubTimeProvider());
        }

        public MergeGameDbContext Db { get; }
        public Guid PlayerId { get; }
        public GameBootstrapService Service { get; }

        public static async Task<Fixture> CreateAsync(bool withPlayer)
        {
            var options = new DbContextOptionsBuilder<MergeGameDbContext>()
                .UseInMemoryDatabase($"game-bootstrap-{Guid.NewGuid()}").Options;
            var db = new MergeGameDbContext(options);
            var playerId = Guid.NewGuid();
            if (withPlayer)
            {
                db.Players.Add(Player.CreateGuest(playerId, new string('A', 64), Now.UtcDateTime));
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
            return new Fixture(db, playerId);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class StubFriendCodeGenerator : IFriendCodeGenerator
    {
        public string Generate() => "ABCDEFGH";
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
