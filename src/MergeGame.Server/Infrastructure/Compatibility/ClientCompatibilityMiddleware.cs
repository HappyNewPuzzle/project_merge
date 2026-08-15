using MergeGame.Server.Domain.Content;

namespace MergeGame.Server.Infrastructure.Compatibility;

/// <summary>
/// 보호된 플레이 API의 Unity 버전을 검사하고 모든 응답에 서버·콘텐츠 버전을 표시합니다.
/// 인증·공개 콘텐츠·관리자 API는 로그인 및 복구 경로를 막지 않도록 검사 대상에서 제외합니다.
/// </summary>
public sealed class ClientCompatibilityMiddleware
{
    public const string ClientVersionHeader = "X-Client-Version";
    public const string ServerVersionHeader = "X-Server-Version";
    public const string ContentVersionHeader = "X-Content-Version";
    private readonly RequestDelegate _next;
    public ClientCompatibilityMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ClientCompatibilityOptions options)
    {
        context.Response.Headers[ServerVersionHeader] = options.ServerVersion;
        context.Response.Headers[ContentVersionHeader] = GameContentVersion.Current;
        if (!RequiresCompatibilityCheck(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var submitted = context.Request.Headers[ClientVersionHeader].ToString();
        if (string.IsNullOrWhiteSpace(submitted) && !options.RequireVersionHeader)
        {
            await _next(context);
            return;
        }

        if (!Version.TryParse(submitted, out var clientVersion)
            || !Version.TryParse(options.MinimumClientVersion, out var minimum)
            || clientVersion < minimum)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status426UpgradeRequired,
                title: "클라이언트 업데이트가 필요합니다.",
                detail: $"최소 지원 버전은 {options.MinimumClientVersion}입니다.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "client_upgrade_required",
                    ["minimumClientVersion"] = options.MinimumClientVersion,
                    ["serverVersion"] = options.ServerVersion,
                    ["contentVersion"] = GameContentVersion.Current
                }).ExecuteAsync(context);
            return;
        }

        await _next(context);
    }

    private static bool RequiresCompatibilityCheck(PathString path)
    {
        if (!path.StartsWithSegments("/api/v1")) return false;
        return !path.StartsWithSegments("/api/v1/auth")
            && !path.StartsWithSegments("/api/v1/players/guest")
            && !path.StartsWithSegments("/api/v1/content")
            && !path.StartsWithSegments("/api/v1/version")
            && !path.StartsWithSegments("/api/v1/admin");
    }
}
