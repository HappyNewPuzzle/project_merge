namespace MergeGame.Server.Infrastructure.Compatibility;

/// <summary>배포 서버 버전과 더 이상 허용하지 않을 최소 Unity 클라이언트 버전을 관리합니다.</summary>
public sealed class ClientCompatibilityOptions
{
    public const string SectionName = "ClientCompatibility";
    public string ServerVersion { get; init; } = "1.0.0";
    public string MinimumClientVersion { get; init; } = "0.1.0";
    public bool RequireVersionHeader { get; init; }
}

public static class ClientCompatibilityServiceExtensions
{
    public static IServiceCollection AddClientCompatibility(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(ClientCompatibilityOptions.SectionName)
            .Get<ClientCompatibilityOptions>() ?? new ClientCompatibilityOptions();
        if (!Version.TryParse(options.ServerVersion, out _)
            || !Version.TryParse(options.MinimumClientVersion, out _))
            throw new InvalidOperationException("ClientCompatibility 버전은 major.minor.patch 형식이어야 합니다.");
        services.AddSingleton(options);
        return services;
    }
}
