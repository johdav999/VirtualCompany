using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesRoomMediaOptions
{
    public const string SectionName = "SalesBrowserRoom";
    public bool Enabled { get; set; }
    public string Route { get; set; } = SalesRoomMediaRoutes.LiveKit;
    public string Url { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public int TokenLifetimeSeconds { get; set; } = 120;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaximumBufferedFrames { get; set; } = 25;
    public int MaximumSessionMinutes { get; set; } = 60;
    public int MaximumLocalRooms { get; set; } = 50;
    public string? ConfigurationProblem => Route != SalesRoomMediaRoutes.LiveKit ? "route_not_supported" :
        !Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme != "wss" ||
        !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/" ? "invalid_endpoint" :
        string.IsNullOrWhiteSpace(ApiKey) || Encoding.UTF8.GetByteCount(ApiSecret) < 32 ? "credentials_missing" :
        TokenLifetimeSeconds is < 30 or > 300 || RequestTimeoutSeconds is < 2 or > 60 ||
        MaximumBufferedFrames is < 5 or > 100 || MaximumSessionMinutes is < 1 or > 60 ||
        MaximumLocalRooms is < 1 or > 100 ? "invalid_limits" : null;
}

public static class SalesRoomMediaRegistration
{
    public static IServiceCollection AddSalesRoomMedia(this IServiceCollection services, IConfiguration configuration)
    {
        // Optional provider settings never validate-on-start or load native libraries during DI.
        services.AddOptions<SalesRoomMediaOptions>().Bind(configuration.GetSection(SalesRoomMediaOptions.SectionName));
        services.AddHttpClient("SalesBrowserRoom.LiveKit", client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<LiveKitSalesRoomMediaTransport>();
        services.AddSingleton<ISalesRoomMediaTransport>(p=>p.GetRequiredService<LiveKitSalesRoomMediaTransport>());
        services.AddSingleton<ISalesRoomProviderInspection>(p=>p.GetRequiredService<LiveKitSalesRoomMediaTransport>());
        services.AddOptions<SalesRoomLifecycleOptions>().Bind(configuration.GetSection(SalesRoomLifecycleOptions.SectionName));
        services.AddScoped<ISalesBrowserRoomService, SalesBrowserRoomService>();
        services.AddScoped<ISalesBrowserMeetingScheduling, SalesBrowserMeetingScheduling>();
        services.AddScoped<ISalesRoomWorkDispatcher, SalesRoomWorkDispatcher>();
        services.AddSingleton<ISalesRoomWebhookVerifier, LiveKitSalesRoomWebhookVerifier>();
        return services;
    }
}
