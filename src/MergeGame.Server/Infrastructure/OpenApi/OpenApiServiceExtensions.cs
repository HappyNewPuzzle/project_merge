using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MergeGame.Server.Infrastructure.OpenApi;

/// <summary>OpenAPI 문서 생성 규칙을 애플리케이션 조립 코드에서 분리합니다.</summary>
public static class OpenApiServiceExtensions
{
    public static IServiceCollection AddMergeGameOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(ApiContract.DocumentName, new OpenApiInfo
            {
                Title = "Merge Game API",
                Version = ApiContract.Version,
                Description = "Unity 머지 게임 클라이언트를 위한 서버 권위형 HTTP API입니다."
            });

            // Swagger UI의 Authorize 버튼에 JWT 원문만 입력하면 Bearer 헤더로 전송됩니다.
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "POST /api/v1/auth/guest에서 발급받은 accessToken을 입력합니다."
            });
            options.OperationFilter<BearerSecurityOperationFilter>();

            // 이미 작성한 상세 XML 주석을 API 설명과 DTO 스키마 설명에 재사용합니다.
            var xmlPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }
}

/// <summary>인증이 필요한 작업에만 OpenAPI Bearer 보안 요구사항을 표시합니다.</summary>
internal sealed class BearerSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();
        if (allowsAnonymous || !requiresAuthorization)
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }] = Array.Empty<string>()
            }
        ];
    }
}
