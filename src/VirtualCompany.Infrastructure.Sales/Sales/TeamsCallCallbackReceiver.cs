using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsCallCallbackReceiver(VirtualCompanyDbContext db, ICompanyOutboxEnqueuer outbox, TimeProvider timeProvider)
    : ITeamsCallCallbackReceiver
{
    public async Task<TeamsCallCallbackReceiveResult> ReceiveAsync(TeamsCallbackIdentity identity, Guid? localCallId, long? joinGeneration, Stream body, CancellationToken ct)
    {
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        if (!json.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
            return new(true, 0, 0, 0);
        var accepted = 0; var duplicates = 0; var ignored = 0;
        foreach (var item in values.EnumerateArray())
        {
            var resource = Text(item, "resourceUrl") ?? Text(item, "resource") ?? string.Empty;
            var data = item.TryGetProperty("resourceData", out var d) ? d : default;
            var providerId = data.ValueKind == JsonValueKind.Object ? Text(data, "id") : null;
            var state = data.ValueKind == JsonValueKind.Object ? Text(data, "state") : null;
            var version = data.ValueKind == JsonValueKind.Object ? Text(data, "@odata.etag") ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(state) || state.Length > 64 || version.Length > 160) { ignored++; continue; }
            TeamsMeetingCall? call = null;
            if (localCallId.HasValue) call = await db.TeamsMeetingCalls.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == identity.CompanyId && x.Id == localCallId, ct);
            if (call is null && !string.IsNullOrWhiteSpace(providerId)) call = await db.TeamsMeetingCalls.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == identity.CompanyId && x.ProviderCallId == providerId, ct);
            if (call is null || call.RegistrationId != identity.RegistrationId) { ignored++; continue; }
            if (joinGeneration.HasValue && call.JoinGeneration != joinGeneration.Value) { ignored++; continue; }
            if (!string.IsNullOrWhiteSpace(providerId) && string.IsNullOrWhiteSpace(call.ProviderCallId))
                call.BindProviderCall(providerId, $"graph-call:{Hash(providerId)[..12].ToLowerInvariant()}", timeProvider.GetUtcNow().UtcDateTime);
            var sequence = Sequence(version);
            var key = Hash($"{identity.CompanyId:N}|{resource}|{providerId}|{version}|{state}");
            if (await db.TeamsCallNotificationReceipts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == identity.CompanyId && x.EventKey == key, ct))
            { duplicates++; TeamsCallTelemetry.CallbackDuplicates.Add(1); continue; }
            var receipt = new TeamsCallNotificationReceipt(Guid.NewGuid(), identity.CompanyId, call.Id, key,
                string.IsNullOrWhiteSpace(version) ? "unversioned" : version, sequence, state, timeProvider.GetUtcNow().UtcDateTime);
            db.TeamsCallNotificationReceipts.Add(receipt);
            outbox.Enqueue(identity.CompanyId, CompanyOutboxTopics.TeamsCallCallbackProcessingRequested,
                new TeamsCallCallbackWorkItem(identity.CompanyId, call.Id, receipt.Id, null),
                idempotencyKey: $"teams-callback:{identity.CompanyId:N}:{key}", messageType: nameof(TeamsCallCallbackWorkItem));
            accepted++; TeamsCallTelemetry.Callbacks.Add(1);
        }
        await db.SaveChangesAsync(ct);
        return new(false, accepted, duplicates, ignored);
    }
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long Sequence(string value)
    {
        if (long.TryParse(value.Trim('W', '/', '"'), out var n)) return n;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value)); return Math.Abs(BitConverter.ToInt64(bytes, 0));
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
