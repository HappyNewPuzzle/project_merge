using MergeGame.Server.Domain.Boards;

namespace MergeGame.Server.Application.Boards;

/// <summary>
/// HTTP 계층에 전달할 현재 보드 전체 상태입니다.
/// </summary>
/// <param name="PlayerId">보드 소유 플레이어 ID입니다.</param>
/// <param name="Width">보드 가로 슬롯 수입니다.</param>
/// <param name="Height">보드 세로 슬롯 수입니다.</param>
/// <param name="Revision">다음 변경 요청에서 expectedRevision으로 보낼 버전입니다.</param>
/// <param name="Items">슬롯 순서로 정렬된 현재 아이템 목록입니다.</param>
public sealed record BoardState(
    Guid PlayerId,
    int Width,
    int Height,
    long Revision,
    IReadOnlyList<BoardItemState> Items);

/// <summary>
/// 클라이언트에 노출할 보드 아이템 상태입니다.
/// </summary>
public sealed record BoardItemState(
    Guid ItemId,
    int SlotIndex,
    string ChainId,
    int Level,
    string Name,
    bool IsMaxLevel);

/// <summary>
/// 도메인 보드를 안전한 응답 상태로 변환합니다.
/// </summary>
public static class BoardStateMapper
{
    /// <summary>
    /// 서버 카탈로그의 표시 정보와 보드 인스턴스를 결합합니다.
    /// </summary>
    public static BoardState Map(PlayerBoard board, IItemCatalog itemCatalog)
    {
        var items = board.Items
            .OrderBy(item => item.SlotIndex)
            .Select(item =>
            {
                if (!itemCatalog.TryGet(item.ChainId, item.Level, out var definition))
                {
                    throw new InvalidOperationException(
                        $"알 수 없는 아이템 정의입니다: {item.ChainId}:{item.Level}");
                }

                return new BoardItemState(
                    item.Id,
                    item.SlotIndex,
                    item.ChainId,
                    item.Level,
                    definition.Name,
                    definition.IsMaxLevel);
            })
            .ToArray();

        return new BoardState(
            board.PlayerId,
            PlayerBoard.Width,
            PlayerBoard.Height,
            board.Revision,
            items);
    }
}
