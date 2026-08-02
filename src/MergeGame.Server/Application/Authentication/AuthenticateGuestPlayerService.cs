using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Authentication;

/// <summary>
/// 플레이어 ID와 게스트 토큰을 검증하고 짧은 수명의 JWT 액세스 토큰을 발급합니다.
/// </summary>
public sealed class AuthenticateGuestPlayerService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IJwtTokenIssuer _tokenIssuer;

    /// <summary>
    /// 플레이어 저장소와 JWT 발급기를 주입받습니다.
    /// </summary>
    public AuthenticateGuestPlayerService(
        MergeGameDbContext dbContext,
        IJwtTokenIssuer tokenIssuer)
    {
        _dbContext = dbContext;
        _tokenIssuer = tokenIssuer;
    }

    /// <summary>
    /// 제출된 게스트 자격 증명이 올바르면 액세스 토큰을 반환합니다.
    /// </summary>
    /// <param name="playerId">게스트 생성 시 받은 플레이어 ID입니다.</param>
    /// <param name="guestToken">게스트 생성 시 한 번 받은 원본 토큰입니다.</param>
    /// <param name="cancellationToken">요청 종료 시 DB 조회를 중단하는 토큰입니다.</param>
    /// <returns>성공 시 토큰 결과, 실패 시 null입니다.</returns>
    public async Task<GuestAuthenticationResult?> ExecuteAsync(
        Guid playerId,
        string guestToken,
        CancellationToken cancellationToken = default)
    {
        if (playerId == Guid.Empty || string.IsNullOrWhiteSpace(guestToken))
        {
            return null;
        }

        // 로그인 검증은 엔티티를 변경하지 않으므로 추적을 꺼 메모리와 CPU 사용을 줄입니다.
        var player = await _dbContext.Players
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == playerId,
                cancellationToken);

        if (player is null
            || !GuestTokenHasher.Matches(guestToken, player.GuestTokenHash))
        {
            // 계정 존재 여부를 노출하지 않도록 두 실패 경우 모두 동일한 null 결과를 사용합니다.
            return null;
        }

        // 정지된 계정에는 새 JWT와 refresh session이 발급되지 않도록 로그인 단계에서 차단합니다.
        if (await _dbContext.PlayerModerations.AsNoTracking()
            .AnyAsync(moderation => moderation.PlayerId == playerId && moderation.IsSuspended, cancellationToken))
            return null;

        var accessToken = _tokenIssuer.Issue(player.Id);
        return new GuestAuthenticationResult(
            player.Id,
            accessToken.Token,
            accessToken.ExpiresAtUtc);
    }
}

/// <summary>
/// 성공한 게스트 로그인 결과입니다.
/// </summary>
/// <param name="PlayerId">인증된 플레이어 식별자입니다.</param>
/// <param name="AccessToken">보호 API 호출에 사용할 서명된 JWT입니다.</param>
/// <param name="ExpiresAtUtc">JWT가 만료되는 UTC 시각입니다.</param>
public sealed record GuestAuthenticationResult(
    Guid PlayerId,
    string AccessToken,
    DateTime ExpiresAtUtc);
