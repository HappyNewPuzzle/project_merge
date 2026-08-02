using MergeGame.Server.Application.Administration;
using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Administration;

/// <summary>운영 조회가 게임 상태 요약을 정확히 계산하고 비밀 필드를 DTO에 노출하지 않는지 확인합니다.</summary>
public sealed class AdminQueryServicesTests
{
    [Fact]
    public async Task PlayerSummary_ReturnsEconomyAndBoardCountsWithoutCredentialFields()
    {
        var now = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        await using var db = new MergeGameDbContext(new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"admin-{Guid.NewGuid()}").Options);
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), now.UtcDateTime);
        db.Players.Add(player); db.PlayerEconomies.Add(PlayerEconomy.CreateInitial(player.Id, now.UtcDateTime));
        db.PlayerBoards.Add(PlayerBoard.CreateInitial(player.Id, now.UtcDateTime));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var result = await new GetAdminPlayerSummaryService(db, new StubTimeProvider(now)).ExecuteAsync(player.Id);

        Assert.NotNull(result);
        Assert.Equal(100, result.Energy);
        Assert.Equal(2, result.BoardItemCount);
        Assert.DoesNotContain(typeof(AdminPlayerSummary).GetProperties(),
            property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
