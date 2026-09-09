using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
namespace VirtualCompany.Api;
public static class SalesRoomApiRegistration
{
    public static IServiceCollection AddSalesRoomApi(this IServiceCollection services)
    {
        services.AddRateLimiter(options => options.AddPolicy("sales-room-access", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })));
        return services;
    }
}
