using System.Security.Cryptography;
using System.Text;
using Livekit.Server.Sdk.Dotnet;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class LiveKitSalesRoomMediaTransport(
    IOptions<SalesRoomMediaOptions> configured, IHttpClientFactory clients) : ISalesRoomMediaTransport, ISalesRoomProviderInspection, IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim connections = new(1, 1);
    private readonly Dictionary<string, LiveKitSalesRoomMediaConnection> active = new();
    private bool closed;
    private SalesRoomMediaOptions Options => configured.Value;

    public SalesRoomMediaReadiness GetReadiness(bool probeNative = false)
    {
        var o = Options;
        var reason = !o.Enabled ? "disabled" : o.ConfigurationProblem;
        if (reason != null) return new(o.Route, o.Enabled, o.ConfigurationProblem == null, false, "unavailable", reason);
        if (!probeNative) return new(o.Route, true, true, false, "configured_unverified", "native_not_probed");
        try
        {
            using var source = new LiveKit.Rtc.AudioSource(48000, 1, 100);
            source.ClearQueue();
            return new(o.Route, true, true, true, "native_ready_live_unverified", null);
        }
        catch { return new(o.Route, true, true, false, "unavailable", "native_runtime_unavailable"); }
    }
    private void RequireConfigured()
    {
        if (!Options.Enabled || Options.ConfigurationProblem != null)
            throw new SalesRoomMediaException(!Options.Enabled ? "disabled" : Options.ConfigurationProblem!);
    }
    public static string RoomName(SalesRoomMediaScope scope)
    {
        scope.Validate();
        return "vc-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope.CompanyId:N}:{scope.RoomId:N}"))).ToLowerInvariant();
    }
    public static string Identity(SalesRoomMediaParticipant participant)
    {
        if (participant.ParticipantId == Guid.Empty || participant.Generation < 1)
            throw new ArgumentException("A valid participant and generation are required.");
        return $"{(participant.IsAgent ? "agent" : "human")}-{participant.ParticipantId:N}-{participant.Generation}";
    }
    private RoomServiceClient Client() => new(new UriBuilder(Options.Url) { Scheme = "https" }.Uri.AbsoluteUri,
        Options.ApiKey, Options.ApiSecret, clients.CreateClient("SalesBrowserRoom.LiveKit"));

    private async Task<T> Provider<T>(Func<Task<T>> action, bool mutation, CancellationToken ct)
    {
        RequireConfigured(); ct.ThrowIfCancellationRequested();
        try { return await action().WaitAsync(TimeSpan.FromSeconds(Options.RequestTimeoutSeconds), ct); }
        catch (SalesRoomMediaException) { throw; }
        // The SDK management calls lack CancellationToken. Once dispatched, timeout/cancellation
        // is ambiguous. Never automatically retry a write or leak SDK payloads/credentials.
        catch { throw new SalesRoomMediaException(mutation ? "provider_outcome_unknown" : "provider_read_failed", mutation); }
    }
    public async Task<SalesRoomProviderRoom?> InspectRoomAsync(SalesRoomMediaScope scope, CancellationToken cancellationToken)
    {
        var name = RoomName(scope);
        var request = new ListRoomsRequest(); request.Names.Add(name);
        var result = await Provider(() => Client().ListRooms(request), false, cancellationToken);
        var room = result.Rooms.SingleOrDefault(r => r.Name == name);
        return room == null ? null : new(room.Name, room.Metadata, checked((int)room.MaxParticipants));
    }
    public async Task<SalesRoomProviderRoom> EnsureRoomAsync(SalesRoomMediaScope scope, Guid operationId,
        int maximumParticipants, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || maximumParticipants is < 2 or > 7) throw new ArgumentException("Invalid room operation or participant limit.");
        var operation = operationId.ToString("N");
        var existing = await InspectRoomAsync(scope, cancellationToken);
        if (existing != null)
        {
            if (existing.OperationId != operation || existing.MaximumParticipants != maximumParticipants)
                throw new SalesRoomMediaException("room_binding_conflict", true);
            return existing;
        }
        var result = await Provider(() => Client().CreateRoom(new CreateRoomRequest
        {
            Name = RoomName(scope), Metadata = operation, MaxParticipants = (uint)maximumParticipants,
            EmptyTimeout = 604800, DepartureTimeout = 60
        }), true, cancellationToken);
        if (result.Metadata != operation || result.MaxParticipants != maximumParticipants)
            throw new SalesRoomMediaException("room_binding_conflict", true);
        return new(result.Name, result.Metadata, (int)result.MaxParticipants);
    }
    public async Task DeleteRoomAsync(SalesRoomMediaScope scope, CancellationToken cancellationToken)
    {
        await connections.WaitAsync(cancellationToken);
        try { if (active.TryGetValue(RoomName(scope), out var connection)) await connection.DisposeAsync(); }
        finally { connections.Release(); }
        if (await InspectRoomAsync(scope, cancellationToken) == null) return;
        await Provider(() => Client().DeleteRoom(new DeleteRoomRequest { Room = RoomName(scope) }), true, cancellationToken);
    }
    public SalesRoomMediaToken IssueToken(SalesRoomMediaScope scope, SalesRoomMediaParticipant participant)
    {
        RequireConfigured();
        var identity = Identity(participant);
        var grants = new VideoGrants
        {
            RoomJoin = true, Room = RoomName(scope), CanSubscribe = true,
            CanPublish = true, CanPublishData = false, CanUpdateOwnMetadata = false,
            CanPublishSources = participant.IsAgent ? new List<string> { "microphone" } : new List<string> { "microphone", "camera", "screen_share", "screen_share_audio" }
        };
        var seconds = Options.TokenLifetimeSeconds;
        if (participant.ExpiresAt is { } deadline) seconds = Math.Min(seconds, (int)Math.Floor((deadline - DateTimeOffset.UtcNow).TotalSeconds) - 1);
        if (seconds < 1) throw new SalesRoomMediaException("participant_expired");
        var expires = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var token = new AccessToken(Options.ApiKey, Options.ApiSecret).WithIdentity(identity)
            .WithGrants(grants).WithTtl(TimeSpan.FromSeconds(seconds)).ToJwt();
        return new(Options.Url, token, identity, expires);
    }
    public async Task RemoveParticipantAsync(SalesRoomMediaScope scope, SalesRoomMediaParticipant participant, CancellationToken cancellationToken)
    {
        await connections.WaitAsync(cancellationToken);
        try
        {
            if (active.TryGetValue(RoomName(scope), out var connection))
            {
                if (participant.IsAgent) await connection.DisposeAsync();
                else await connection.RevokeInputAsync(participant.ParticipantId, cancellationToken);
            }
        }
        finally { connections.Release(); }
        await Provider(() => Client().RemoveParticipant(new RoomParticipantIdentity
            { Room = RoomName(scope), Identity = Identity(participant), RevokeTokenTs = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds() }), true, cancellationToken);
    }
    public async Task<ISalesRoomMediaConnection> ConnectAgentAsync(SalesRoomMediaScope scope, SalesRoomMediaParticipant agent,
        IReadOnlyCollection<SalesRoomMediaParticipant> consentedHumans, CancellationToken cancellationToken)
    {
        RequireConfigured();
        if (!agent.IsAgent || consentedHumans.Count is < 1 or > 6 || consentedHumans.Any(p => p.IsAgent) ||
            consentedHumans.Select(p => p.ParticipantId).Distinct().Count() != consentedHumans.Count)
            throw new ArgumentException("One agent and distinct consented human participants are required.");
        foreach (var human in consentedHumans) Identity(human);
        var room = RoomName(scope);
        await connections.WaitAsync(cancellationToken);
        try
        {
            if (closed) throw new ObjectDisposedException(nameof(LiveKitSalesRoomMediaTransport));
            if (active.TryGetValue(room, out var prior))
            {
                if (prior.GetStatistics().State != "stopped") throw new SalesRoomMediaException("room_already_connected");
                active.Remove(room);
            }
            foreach (var ended in active.Where(p => p.Value.GetStatistics().State == "stopped").Select(p => p.Key).ToArray()) active.Remove(ended);
            if (active.Count >= Options.MaximumLocalRooms) throw new SalesRoomMediaException("local_room_capacity");
            var token = IssueToken(scope, agent);
            var connection = await LiveKitSalesRoomMediaConnection.ConnectAsync(token, consentedHumans, Options, cancellationToken);
            active.Add(room, connection);
            return connection;
        }
        finally { connections.Release(); }
    }
    public async Task<IReadOnlyList<string>> ParticipantsAsync(SalesRoomMediaScope scope, CancellationToken ct)
    {
        if(await InspectRoomAsync(scope,ct)==null)return Array.Empty<string>();
        var result=await Provider(()=>Client().ListParticipants(new ListParticipantsRequest {Room=RoomName(scope)}),false,ct);
        return result.Participants.Select(x=>x.Identity).ToArray();
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await connections.WaitAsync();
        try
        {
            if (closed) return;
            closed = true;
            foreach (var connection in active.Values) await connection.DisposeAsync();
            active.Clear();
        }
        finally { connections.Release(); }
    }

}
