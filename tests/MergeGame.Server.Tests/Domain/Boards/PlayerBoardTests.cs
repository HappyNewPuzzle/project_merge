using MergeGame.Server.Domain.Boards;
using MergeGame.Server.Infrastructure.Items;

namespace MergeGame.Server.Tests.Domain.Boards;

/// <summary>
/// DB나 HTTP와 무관한 보드 머지 핵심 규칙을 빠르게 검증합니다.
/// </summary>
public sealed class PlayerBoardTests
{
    private static readonly DateTime InitialTime =
        new(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 같은 1레벨 아이템 두 개를 머지하면 source가 사라지고 target이 2레벨이 되는지 확인합니다.
    /// </summary>
    [Fact]
    public void TryMerge_WithMatchingItems_ConsumesSourceAndUpgradesTarget()
    {
        var board = PlayerBoard.CreateInitial(Guid.NewGuid(), InitialTime);
        var catalog = new InMemoryItemCatalog();

        var result = board.TryMerge(
            sourceSlot: 0,
            targetSlot: 1,
            expectedRevision: 1,
            catalog,
            InitialTime.AddMinutes(1));

        Assert.True(result.Success);
        Assert.Equal(2, board.Revision);
        Assert.Single(board.Items);
        var mergedItem = Assert.Single(board.Items);
        Assert.Equal(1, mergedItem.SlotIndex);
        Assert.Equal(2, mergedItem.Level);
        Assert.Equal(InitialTime.AddMinutes(1), board.UpdatedAtUtc);
    }

    /// <summary>
    /// 오래된 revision 요청은 상태를 전혀 변경하지 않고 충돌로 거부하는지 확인합니다.
    /// </summary>
    [Fact]
    public void TryMerge_WithStaleRevision_DoesNotMutateBoard()
    {
        var board = PlayerBoard.CreateInitial(Guid.NewGuid(), InitialTime);

        var result = board.TryMerge(
            sourceSlot: 0,
            targetSlot: 1,
            expectedRevision: 0,
            new InMemoryItemCatalog(),
            InitialTime.AddMinutes(1));

        Assert.False(result.Success);
        Assert.Equal(BoardMergeError.StaleRevision, result.Error);
        Assert.Equal(1, board.Revision);
        Assert.Equal(2, board.Items.Count);
        Assert.All(board.Items, item => Assert.Equal(1, item.Level));
    }

    /// <summary>
    /// 같은 슬롯을 source와 target으로 보내는 잘못된 요청을 거부하는지 확인합니다.
    /// </summary>
    [Fact]
    public void TryMerge_WithSameSlot_ReturnsSameSlotError()
    {
        var board = PlayerBoard.CreateInitial(Guid.NewGuid(), InitialTime);

        var result = board.TryMerge(
            sourceSlot: 0,
            targetSlot: 0,
            expectedRevision: 1,
            new InMemoryItemCatalog(),
            InitialTime);

        Assert.False(result.Success);
        Assert.Equal(BoardMergeError.SameSlot, result.Error);
        Assert.Equal(2, board.Items.Count);
    }

    /// <summary>
    /// 카탈로그가 현재 단계를 최종 단계로 정의하면 동일 아이템도 머지할 수 없는지 확인합니다.
    /// </summary>
    [Fact]
    public void TryMerge_AtCatalogMaxLevel_ReturnsMaxLevelError()
    {
        var board = PlayerBoard.CreateInitial(Guid.NewGuid(), InitialTime);

        var result = board.TryMerge(
            sourceSlot: 0,
            targetSlot: 1,
            expectedRevision: 1,
            new MaxLevelCatalog(),
            InitialTime);

        Assert.False(result.Success);
        Assert.Equal(BoardMergeError.MaxLevelReached, result.Error);
        Assert.Equal(2, board.Items.Count);
    }

    [Fact]
    public void TryApplyAction_ToEmptySlot_MovesSameItemInstance()
    {
        var board = PlayerBoard.CreateInitial(Guid.NewGuid(), InitialTime);
        var sourceId = board.Items.Single(value => value.SlotIndex == 0).Id;

        var result = board.TryApplyAction(0, 2, 1, new InMemoryItemCatalog(), InitialTime);

        Assert.True(result.Success);
        Assert.Equal(BoardActionType.Moved, result.Action);
        Assert.Equal(sourceId, board.Items.Single(value => value.SlotIndex == 2).Id);
        Assert.Equal(2, board.Revision);
    }

    [Fact]
    public void TryApplyAction_WithMatchingTarget_PerformsMerge()
    {
        var board = PlayerBoard.CreateInitial(Guid.NewGuid(), InitialTime);

        var result = board.TryApplyAction(0, 1, 1, new InMemoryItemCatalog(), InitialTime);

        Assert.True(result.Success);
        Assert.Equal(BoardActionType.Merged, result.Action);
        Assert.Equal(2, Assert.Single(board.Items).Level);
    }

    [Fact]
    public void TryApplyAction_WithDifferentItems_SwapsInstanceSlots()
    {
        var board = PlayerBoard.CreateInitial(Guid.NewGuid(), InitialTime);
        var catalog = new InMemoryItemCatalog();
        Assert.True(board.TryMerge(0, 1, 1, catalog, InitialTime).Success);
        Assert.True(board.TryAddGeneratedItem(0, 2, "garden", 1, catalog, InitialTime).Success);
        var levelOneId = board.Items.Single(value => value.Level == 1).Id;
        var levelTwoId = board.Items.Single(value => value.Level == 2).Id;

        var result = board.TryApplyAction(0, 1, 3, catalog, InitialTime);

        Assert.Equal(BoardActionType.Swapped, result.Action);
        Assert.Equal(1, board.Items.Single(value => value.Id == levelOneId).SlotIndex);
        Assert.Equal(0, board.Items.Single(value => value.Id == levelTwoId).SlotIndex);
    }

    /// <summary>
    /// 초기 아이템을 테스트 목적상 최대 단계로 취급하는 카탈로그 대역입니다.
    /// </summary>
    private sealed class MaxLevelCatalog : IItemCatalog
    {
        public bool TryGet(
            string chainId,
            int level,
            out ItemDefinition definition)
        {
            definition = new ItemDefinition(chainId, level, "Max Item", IsMaxLevel: true);
            return true;
        }

        public bool TryGetNext(
            string chainId,
            int currentLevel,
            out ItemDefinition nextDefinition)
        {
            nextDefinition = null!;
            return false;
        }
    }
}
