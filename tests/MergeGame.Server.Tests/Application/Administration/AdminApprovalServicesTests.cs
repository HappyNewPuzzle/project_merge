using MergeGame.Server.Application.Administration;
using MergeGame.Server.Domain.Economy;
using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Tests.Application.Administration;

/// <summary>
/// 고액 조정 요청자와 승인자가 반드시 분리되고, 승인 재시도에도 코인·감사·원장이
/// 정확히 한 번만 기록되는지 검증합니다.
/// </summary>
public sealed class AdminApprovalServicesTests
{
    [Fact]
    public async Task DifferentApprover_ExecutesOnceWhileRequesterCannotSelfApprove()
    {
        await using var db = new MergeGameDbContext(new DbContextOptionsBuilder<MergeGameDbContext>()
            .UseInMemoryDatabase($"admin-approval-{Guid.NewGuid()}").Options);
        var now = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
        var player = Player.CreateGuest(Guid.NewGuid(), new string('A', 64), now.UtcDateTime);
        db.Players.Add(player);
        db.PlayerEconomies.Add(PlayerEconomy.CreateInitial(player.Id, now.UtcDateTime));
        await db.SaveChangesAsync();
        var time = new StubTimeProvider(now);
        var create = new CreateCoinAdjustmentApprovalService(db, time);
        var adjust = new AdjustPlayerCoinsService(db, time,
            new AdminApiOptions { MaxAbsoluteCoinAdjustment = 10_000 });
        var approve = new ApproveCoinAdjustmentService(db, time, adjust);

        var request = await create.ExecuteAsync(
            "requester-a", "approval-ticket-001", player.Id, 5_000, "고액 고객 보상", 1);
        db.ChangeTracker.Clear();
        var self = await approve.ExecuteAsync(request!.ApprovalId, "requester-a");
        db.ChangeTracker.Clear();
        var approved = await approve.ExecuteAsync(request.ApprovalId, "approver-b");
        db.ChangeTracker.Clear();
        var replay = await approve.ExecuteAsync(request.ApprovalId, "approver-b");

        Assert.Equal(ApprovalExecutionStatus.SameOperator, self.Status);
        Assert.Equal(ApprovalExecutionStatus.Succeeded, approved.Status);
        Assert.Equal(ApprovalExecutionStatus.Replayed, replay.Status);
        Assert.Equal(5_000, (await db.PlayerEconomies.SingleAsync()).Coins);
        Assert.Single(await db.AdminActionAudits.ToListAsync());
        Assert.Single(await db.EconomyLedgerEntries.ToListAsync());
        var saved = await db.AdminApprovalRequests.SingleAsync();
        Assert.Equal("approved", saved.Status);
        Assert.Equal("approver-b", saved.ApprovedBy);
    }

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
