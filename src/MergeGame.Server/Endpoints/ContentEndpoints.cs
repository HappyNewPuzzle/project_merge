using MergeGame.Server.Application.Content;
using MergeGame.Server.Domain.Content;

namespace MergeGame.Server.Endpoints;

/// <summary>인증 전에도 내려받을 수 있는 버전형 게임 규칙 카탈로그를 제공합니다.</summary>
public static class ContentEndpoints
{
    public static WebApplication MapContentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/content/catalog", GetCatalog)
            .WithTags("Content")
            .WithName("GetContentCatalog")
            .Produces<ContentCatalogResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified);
        return app;
    }

    private static IResult GetCatalog(HttpContext context, GetContentCatalogService service)
    {
        var etag = $"\"{GameContentVersion.Current}\"";
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "public,max-age=300,must-revalidate";
        if (string.Equals(context.Request.Headers.IfNoneMatch, etag, StringComparison.Ordinal))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Ok(service.Execute());
    }
}
