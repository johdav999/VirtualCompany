using System.Threading.Channels;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    // Deterministic transport receipts exercise real worker connection/disposal. They do
    // not represent a live LiveKit participant or audible browser output.
    private sealed class LifetimeTransport(Media media) : ISalesRoomMediaTransport
    {
        public int Connections;
        public Task<ISalesRoomMediaConnection> ConnectAgentAsync(SalesRoomMediaScope scope,
            SalesRoomMediaParticipant agent, IReadOnlyCollection<SalesRoomMediaParticipant> humans, CancellationToken ct)
        { Connections++; media.Disposed = false; return Task.FromResult<ISalesRoomMediaConnection>(media); }
        public SalesRoomMediaReadiness GetReadiness(bool probeNative = false) => new("test", true, true, true, "ready", null);
        public Task<SalesRoomProviderRoom> EnsureRoomAsync(SalesRoomMediaScope s, Guid id, int max, CancellationToken ct) => throw new NotSupportedException();
        public Task<SalesRoomProviderRoom?> InspectRoomAsync(SalesRoomMediaScope s, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteRoomAsync(SalesRoomMediaScope s, CancellationToken ct) => throw new NotSupportedException();
        public SalesRoomMediaToken IssueToken(SalesRoomMediaScope s, SalesRoomMediaParticipant p) => throw new NotSupportedException();
        public Task RemoveParticipantAsync(SalesRoomMediaScope s, SalesRoomMediaParticipant p, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class LifetimePcm : IRealtimeAgentPcmSessionGateway
    {
        public DateTime ExpiresUtc;
        public DateTime? ExpiresUtcAfterFirst;
        public int Connections;
        public int Terminations;
        public RealtimeAgentPcmSessionCreateRequest? Request;
        public long AudioBytes;
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> ClientEvents = new();
        public readonly TaskCompletionSource Connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly Channel<RealtimeAgentPcmOutput> Events = Channel.CreateUnbounded<RealtimeAgentPcmOutput>();
        public Task<RealtimeAgentPcmSessionConnection> CreatePcmSessionAsync(RealtimeAgentPcmSessionCreateRequest r, CancellationToken ct)
        {
            Request = r; Connections++; Connected.TrySetResult();
            return Task.FromResult(new RealtimeAgentPcmSessionConnection("test", "test-session-" + Connections, "test", 24000,
                Connections > 1 ? ExpiresUtcAfterFirst ?? ExpiresUtc : ExpiresUtc));
        }
        public IAsyncEnumerable<RealtimeAgentPcmOutput> ReceiveOutputAsync(string s, CancellationToken ct) => Events.Reader.ReadAllAsync(ct);
        public Task SendInputAudioAsync(string s, ReadOnlyMemory<byte> audio, CancellationToken ct)
        { Interlocked.Add(ref AudioBytes, audio.Length); return Task.CompletedTask; }
        public Task SendClientEventAsync(string s, string json, CancellationToken ct)
        { ClientEvents.Enqueue(json); return Task.CompletedTask; }
        public Task<RealtimeAgentControlResult> CancelPcmResponseAsync(string s, string? r, CancellationToken ct) => Task.FromResult(new RealtimeAgentControlResult(true));
        public Task TerminatePcmSessionAsync(string s, CancellationToken ct) { Terminations++; return Task.CompletedTask; }
    }
}
