using MergeGame.Server.Application.Boards;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MergeGame.Server.Application.Quests;
using MergeGame.Server.Infrastructure.Quests;

namespace MergeGame.Server.Tests.Application.Boards;

/// <summary>통합 보드 액션의 저장, 이벤트 연결과 멱등 재시도를 검증합니다.</summary>
public sealed class ApplyBoardActionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 15, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_MoveThenReplay_ChangesBoardOnce()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.ExecuteAsync(fixture.PlayerId, 0, 2, 1, "move-key");
        fixture.Db.ChangeTracker.Clear();
        var replay = await fixture.Service.ExecuteAsync(fixture.PlayerId, 0, 2, 1, "move-key");

        Assert.Equal("move", first.Response!.Action);
        Assert.True(replay.Response!.Replayed);
        Assert.Equal(first.Response.ResultItem.ItemId, replay.Response.ResultItem.ItemId);
        Assert.Single(await fixture.Db.BoardActionReceipts.ToListAsync());
        Assert.Equal(2, (await fixture.Db.PlayerBoards.SingleAsync()).Revision);
    }

    [Fact]
    public async Task ExecuteAsync_Merge_RecordsGameplayEvent()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ExecuteAsync(fixture.PlayerId, 0, 1, 1, "merge-key");

        Assert.Equal("merge", result.Response!.Action);
        Assert.Single(await fixture.Db.GameplayEvents.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ChangedPayloadWithSameKey_ReturnsConflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.ExecuteAsync(fixture.PlayerId, 0, 2, 1, "same-key");
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.ExecuteAsync(fixture.PlayerId, 1, 3, 2, "same-key");

        Assert.Equal(BoardActionServiceError.IdempotencyKeyConflict, result.Error);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(MergeGameDbContext db, Guid playerId)
        {
            Db = db;
            PlayerId = playerId;
            Service = new ApplyBoardActionService(
                db,
                new InMemoryItemCatalog(),
                new StubTimeProvider(),
                new QuestProgressService(db, new InMemoryQuestCatalog()));
        }

        public MergeGameDbContext Db { get; }
        public Guid PlayerId { get; }
        public ApplyBoardActionService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<MergeGameDbContext>()
                .UseInMemoryDatabase($"board-actions-{Guid.NewGuid()}").Options;
            var db = new MergeGameDbContext(options);
            var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), Now.UtcDateTime);
            db.Players.Add(player);
            db.PlayerBoards.Add(PlayerBoard.CreateInitial(player.Id, Now.UtcDateTime));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(db, player.Id);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
