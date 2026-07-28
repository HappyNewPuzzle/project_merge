using MergeGame.Server.Domain.Players;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Security;

namespace MergeGame.Server.Application.Players;

/// <summary>
/// 새로운 게스트 자격 증명과 플레이어 레코드를 하나의 작업으로 생성합니다.
/// HTTP 표현과 분리되어 있으므로 향후 관리자 도구나 다른 프로토콜에서도 재사용할 수 있습니다.
/// </summary>
public sealed class CreateGuestPlayerService
{
    private readonly MergeGameDbContext _dbContext;
    private readonly IGuestCredentialGenerator _credentialGenerator;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// 게스트 생성에 필요한 저장소, 보안 토큰 생성기, 시간 공급자를 주입받습니다.
    /// </summary>
    public CreateGuestPlayerService(
        MergeGameDbContext dbContext,
        IGuestCredentialGenerator credentialGenerator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _credentialGenerator = credentialGenerator;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// 게스트 플레이어를 DB에 저장하고 클라이언트에 한 번만 보여 줄 원본 토큰을 반환합니다.
    /// </summary>
    /// <param name="cancellationToken">연결 종료나 서버 종료 시 DB 작업을 중단하는 토큰입니다.</param>
    /// <returns>생성된 플레이어 정보와 원본 게스트 토큰입니다.</returns>
    public async Task<CreatedGuestPlayer> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var credential = _credentialGenerator.Generate();
        var playerId = Guid.NewGuid();
        var createdAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var player = Player.CreateGuest(
            playerId,
            credential.TokenHash,
            createdAtUtc);

        _dbContext.Players.Add(player);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreatedGuestPlayer(
            player.Id,
            player.DisplayName,
            credential.RawToken,
            player.CreatedAtUtc);
    }
}

/// <summary>
/// 게스트 플레이어 생성 유스케이스의 결과입니다.
/// </summary>
/// <param name="PlayerId">생성된 플레이어 식별자입니다.</param>
/// <param name="DisplayName">자동으로 생성된 기본 표시 이름입니다.</param>
/// <param name="GuestToken">클라이언트가 안전하게 보관해야 하는 원본 접근 토큰입니다.</param>
/// <param name="CreatedAtUtc">계정 생성 UTC 시각입니다.</param>
public sealed record CreatedGuestPlayer(
    Guid PlayerId,
    string DisplayName,
    string GuestToken,
    DateTime CreatedAtUtc);
