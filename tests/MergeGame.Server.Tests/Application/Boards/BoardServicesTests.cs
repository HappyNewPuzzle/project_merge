using MergeGame.Server.Application.Boards;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Items;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Boards;

/// <summary>
/// 보드 초기화와 머지 서비스가 EF Core 저장 상태를 올바르게 유지하는지 검증합니다.
/// </summary>
public sealed class BoardServicesTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 같은 플레이어가 초기화를 반복해도 보드와 시작 아이템이 중복 생성되지 않는지 확인합니다.
    /// </summary>
    [Fact]
    public async Task Initialize_CalledTwice_CreatesSingleBoard()
    {
        await using var dbContext = CreateDbContext();
        var player = CreatePlayer();
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();

        var service = new InitializePlayerBoardService(
            dbContext,
            new InMemoryItemCatalog(),
            new StubTimeProvider(FixedNow));

        var first = await service.ExecuteAsync(player.Id);
        var second = await service.ExecuteAsync(player.Id);

        Assert.Equal(BoardInitializationStatus.Created, first.Status);
        Assert.Equal(BoardInitializationStatus.AlreadyExists, second.Status);
        Assert.Equal(1, await dbContext.PlayerBoards.CountAsync());
        Assert.Equal(2, await dbContext.BoardItems.CountAsync());
    }

    /// <summary>
    /// 서비스에서 성공한 머지가 DB에도 원본 삭제, 대상 레벨 상승, revision 증가로 저장되는지 확인합니다.
    /// </summary>
    [Fact]
    public async Task Merge_WithCurrentRevision_PersistsAtomicBoardChange()
    {
        await using var dbContext = CreateDbContext();
        var player = CreatePlayer();
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();

        var catalog = new InMemoryItemCatalog();
        var timeProvider = new StubTimeProvider(FixedNow);
        var initializeService = new InitializePlayerBoardService(
            dbContext,
            catalog,
            timeProvider);
        await initializeService.ExecuteAsync(player.Id);

        var mergeService = new MergeBoardItemsService(
            dbContext,
            catalog,
            timeProvider);
        var result = await mergeService.ExecuteAsync(
            player.Id,
            sourceSlot: 0,
            targetSlot: 1,
            expectedRevision: 1);

        dbContext.ChangeTracker.Clear();
        var savedBoard = await dbContext.PlayerBoards
            .Include(board => board.Items)
            .SingleAsync(board => board.PlayerId == player.Id);

        Assert.Equal(BoardMergeServiceStatus.Succeeded, result.Status);
        Assert.Equal(2, savedBoard.Revision);
        var savedItem = Assert.Single(savedBoard.Items);
        Assert.Equal(1, savedItem.SlotIndex);
        Assert.Equal(2, savedItem.Level);
    }

    /// <summary>
    /// 같은 revision을 읽은 두 DB 컨텍스트가 동시에 저장하면 두 번째 저장이 동시성 예외로 차단되는지 확인합니다.
    /// </summary>
    [Fact]
    public async Task SaveChanges_WithConcurrentBoardRevision_RejectsSecondWriter()
    {
        var databaseName = $"board-concurrency-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var player = CreatePlayer();

        await using (var seedContext = new MergeGameDbContext(options))
        {
            seedContext.Players.Add(player);
            seedContext.PlayerBoards.Add(
                PlayerBoard.CreateInitial(
                    player.Id,
                    FixedNow.UtcDateTime));
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = new MergeGameDbContext(options);
        await using var secondContext = new MergeGameDbContext(options);
        var firstBoard = await firstContext.PlayerBoards
            .Include(board => board.Items)
            .SingleAsync();
        var secondBoard = await secondContext.PlayerBoards
            .Include(board => board.Items)
            .SingleAsync();
        var catalog = new InMemoryItemCatalog();

        Assert.True(firstBoard.TryMerge(0, 1, 1, catalog, FixedNow.UtcDateTime).Success);
        Assert.True(secondBoard.TryMerge(0, 1, 1, catalog, FixedNow.UtcDateTime).Success);

        await firstContext.SaveChangesAsync();

        // revision=1을 WHERE 조건으로 사용하는 두 번째 UPDATE는 이미 revision=2인 행을 찾지 못해야 합니다.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync());
    }

    private static MergeGameDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"board-services-{Guid.NewGuid()}")
            .Options;
        return new MergeGameDbContext(options);
    }

    private static Player CreatePlayer()
    {
        return Player.CreateGuest(
            Guid.NewGuid(),
            new string('A', 64),
            FixedNow.UtcDateTime);
    }

    /// <summary>
    /// 생성 및 수정 시각을 재현 가능하게 고정하는 테스트 시간 공급자입니다.
    /// </summary>
    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public StubTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
