using Livekit.Server.Sdk.Dotnet;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
namespace VirtualCompany.Infrastructure.Sales;
public sealed class LiveKitSalesRoomWebhookVerifier(IOptions<SalesRoomMediaOptions> configured, TimeProvider clock) : ISalesRoomWebhookVerifier
{
    public SalesRoomWebhook Verify(string body, string authorization)
    {
        if (body.Length > 65536 || string.IsNullOrWhiteSpace(authorization) || authorization.Length > 8192) throw new SalesRoomAccessException("webhook_invalid", 401);
        try
        {
            var o = configured.Value;
            var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : authorization;
            var e = new WebhookReceiver(o.ApiKey, o.ApiSecret).Receive(body, token, skipAuth: false);
            if (string.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 100 || e.Room == null || e.Room.Name.Length > 100 || e.CreatedAt > clock.GetUtcNow().AddMinutes(1).ToUnixTimeSeconds() || e.CreatedAt < clock.GetUtcNow().AddDays(-7).ToUnixTimeSeconds())
                throw new InvalidOperationException();
            if (e.Event is not ("room_finished" or "participant_joined" or "participant_left")) return new(e.Id, "ignored", e.Room.Name, null, e.CreatedAt);
            if (e.Participant?.Identity.Length > 100) throw new InvalidOperationException();
            return new(e.Id, e.Event, e.Room.Name, e.Participant?.Identity, e.CreatedAt);
        }
        catch { throw new SalesRoomAccessException("webhook_invalid", 401); }
    }
}
