using MergeGame.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Players;

/// <summary>
/// 인증된 플레이어에게 노출할 최소 프로필을 조회합니다.
/// 인증 비밀인 GuestTokenHash는 결과에 포함하지 않습니다.
/// </summary>
public sealed class GetPlayerProfileService
{
    private readonly MergeGameDbContext _dbContext;

    /// <summary>
    /// 플레이어 조회에 사용할 게임 DbContext를 주입받습니다.
    /// </summary>
    public GetPlayerProfileService(MergeGameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 지정한 플레이어의 공개 가능한 프로필만 읽습니다.
    /// </summary>
    /// <param name="playerId">JWT에서 검증된 플레이어 ID입니다.</param>
    /// <param name="cancellationToken">요청 종료 시 DB 조회를 중단하는 토큰입니다.</param>
    /// <returns>플레이어가 존재하면 프로필, 없으면 null입니다.</returns>
    public Task<PlayerProfile?> ExecuteAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        // 엔티티 전체를 가져오지 않고 필요한 컬럼만 SQL SELECT에 포함시킵니다.
        return _dbContext.Players
            .AsNoTracking()
            .Where(player => player.Id == playerId)
            .Select(player => new PlayerProfile(
                player.Id,
                player.DisplayName,
                player.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

/// <summary>
/// 인증된 본인에게 반환하는 플레이어 프로필입니다.
/// </summary>
/// <param name="PlayerId">플레이어 식별자입니다.</param>
/// <param name="DisplayName">현재 표시 이름입니다.</param>
/// <param name="CreatedAtUtc">계정 생성 UTC 시각입니다.</param>
public sealed record PlayerProfile(
    Guid PlayerId,
    string DisplayName,
    DateTime CreatedAtUtc);
