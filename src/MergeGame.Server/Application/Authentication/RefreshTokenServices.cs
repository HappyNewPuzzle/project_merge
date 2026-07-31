using MergeGame.Server.Domain.Authentication;
using MergeGame.Server.Infrastructure.Authentication;
using MergeGame.Server.Infrastructure.Persistence;
using MergeGame.Server.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MergeGame.Server.Application.Authentication;

/// <summary>로그인 성공 시 새 토큰 계열의 첫 refresh token을 저장합니다.</summary>
public sealed class CreateRefreshSessionService
{
    private readonly MergeGameDbContext _db; private readonly IRefreshTokenGenerator _generator;
    private readonly JwtOptions _options; private readonly TimeProvider _time;
    public CreateRefreshSessionService(MergeGameDbContext db, IRefreshTokenGenerator generator, JwtOptions options, TimeProvider time)
    { _db = db; _generator = generator; _options = options; _time = time; }
    public async Task<RefreshTokenResult> ExecuteAsync(Guid playerId, CancellationToken token = default)
    {
        var now = _time.GetUtcNow().UtcDateTime; var generated = _generator.Generate();
        var session = RefreshTokenSession.Create(playerId, Guid.NewGuid(), generated.TokenHash, now, now.AddDays(_options.RefreshTokenDays));
        _db.RefreshTokenSessions.Add(session); await _db.SaveChangesAsync(token);
        return new(generated.RawToken, session.ExpiresAtUtc);
    }
}

/// <summary>유효 토큰을 한 번만 사용해 새 JWT와 refresh token으로 원자적으로 교환합니다.</summary>
public sealed class RotateRefreshTokenService
{
    private readonly MergeGameDbContext _db; private readonly IRefreshTokenGenerator _generator;
    private readonly IJwtTokenIssuer _jwt; private readonly JwtOptions _options; private readonly TimeProvider _time;
    public RotateRefreshTokenService(MergeGameDbContext db, IRefreshTokenGenerator generator, IJwtTokenIssuer jwt, JwtOptions options, TimeProvider time)
    { _db = db; _generator = generator; _jwt = jwt; _options = options; _time = time; }

    public async Task<TokenRotationResult> ExecuteAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return new(TokenRotationStatus.Invalid, null);
        var hash = GuestTokenHasher.Hash(rawToken); var now = _time.GetUtcNow().UtcDateTime;
        var current = await _db.RefreshTokenSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (current is null || current.ExpiresAtUtc <= now) return new(TokenRotationStatus.Invalid, null);
        if (current.RevokedAtUtc is not null)
        {
            // 이미 회전된 토큰 재사용은 탈취 신호이므로 같은 계열의 활성 토큰까지 모두 폐기합니다.
            var family = await _db.RefreshTokenSessions.Where(x => x.PlayerId == current.PlayerId && x.FamilyId == current.FamilyId).ToListAsync(cancellationToken);
            foreach (var session in family) session.Revoke(now, "reuse_detected");
            await _db.SaveChangesAsync(cancellationToken);
            return new(TokenRotationStatus.ReuseDetected, null);
        }
        var generated = _generator.Generate();
        var replacement = RefreshTokenSession.Create(current.PlayerId, current.FamilyId, generated.TokenHash, now, now.AddDays(_options.RefreshTokenDays));
        current.Revoke(now, "rotated", replacement.Id); _db.RefreshTokenSessions.Add(replacement);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return new(TokenRotationStatus.Invalid, null); }
        var access = _jwt.Issue(current.PlayerId);
        return new(TokenRotationStatus.Succeeded, new TokenPairResult(current.PlayerId, access.Token, access.ExpiresAtUtc, generated.RawToken, replacement.ExpiresAtUtc));
    }
}

/// <summary>로그아웃 시 제출 refresh token을 멱등 폐기합니다.</summary>
public sealed class RevokeRefreshTokenService
{
    private readonly MergeGameDbContext _db; private readonly TimeProvider _time;
    public RevokeRefreshTokenService(MergeGameDbContext db, TimeProvider time) { _db = db; _time = time; }
    public async Task ExecuteAsync(Guid playerId, string rawToken, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = GuestTokenHasher.Hash(rawToken);
        var session = await _db.RefreshTokenSessions.SingleOrDefaultAsync(x => x.PlayerId == playerId && x.TokenHash == hash, token);
        if (session is null) return; session.Revoke(_time.GetUtcNow().UtcDateTime, "logout"); await _db.SaveChangesAsync(token);
    }
}

public sealed record RefreshTokenResult(string RefreshToken, DateTime ExpiresAtUtc);
public sealed record TokenPairResult(Guid PlayerId, string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);
public enum TokenRotationStatus { Succeeded, Invalid, ReuseDetected }
public sealed record TokenRotationResult(TokenRotationStatus Status, TokenPairResult? Tokens);
