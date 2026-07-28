namespace MergeGame.Server.Infrastructure.Authentication;

/// <summary>
/// JWT 검증을 통과한 현재 요청의 플레이어 식별자를 애플리케이션에 제공합니다.
/// </summary>
public interface ICurrentPlayerAccessor
{
    /// <summary>
    /// 현재 요청에서 유효한 플레이어 ID를 읽습니다.
    /// </summary>
    /// <param name="playerId">성공 시 JWT sub 클레임의 플레이어 GUID입니다.</param>
    /// <returns>인증된 플레이어 ID가 존재하고 GUID 형식이면 true입니다.</returns>
    bool TryGetPlayerId(out Guid playerId);
}
