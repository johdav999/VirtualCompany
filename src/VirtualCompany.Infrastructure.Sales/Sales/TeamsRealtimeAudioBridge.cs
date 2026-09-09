using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsRealtimeAudioBridge(
    ITeamsMeetingMediaAdapter teamsMedia,
    IRealtimeAgentPcmSessionGateway pcmGateway,
    IRealtimeAgentSessionGateway eventGateway,
    IServiceScopeFactory scopes,
    ILogger<TeamsRealtimeAudioBridge> logger) : ITeamsRealtimeAudioBridge
{
    private readonly ConcurrentDictionary<string, byte> _cancelledSpeechEvents = new(StringComparer.Ordinal);

    public async Task RunAsync(TeamsMeetingMediaBinding binding, string realtimeProviderSessionId, CancellationToken ct)
    {
        var userId = await LoadIdentityAsync(binding, ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var input = PumpParticipantAudioAsync(binding, realtimeProviderSessionId, linked.Token);
        var output = PumpAgentOutputAsync(binding, userId, realtimeProviderSessionId, linked.Token);
        try
        {
            await Task.WhenAny(input, output);
            linked.Cancel();
            await Task.WhenAll(IgnoreCancellation(input), IgnoreCancellation(output));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "The Teams-to-realtime audio bridge degraded for media session {MediaSessionId}; typed controls remain available.",
                binding.VoiceSessionId);
            await teamsMedia.SuspendAsync(binding.VoiceSessionId, "realtime_bridge_degraded", CancellationToken.None);
            throw;
        }
        finally
        {
            _cancelledSpeechEvents.Clear();
            await teamsMedia.TerminateAsync(binding.VoiceSessionId, "realtime_bridge_stopped", CancellationToken.None);
            await pcmGateway.TerminatePcmSessionAsync(realtimeProviderSessionId, CancellationToken.None);
        }
    }

    private async Task PumpParticipantAudioAsync(TeamsMeetingMediaBinding binding, string providerSessionId, CancellationToken ct)
    {
        await foreach (var frame in teamsMedia.ReceiveAudioAsync(binding.VoiceSessionId, ct))
        {
            using (var scope = scopes.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ITeamsMeetingMediaBindingAuthorizer>()
                    .AuthorizeAsync(binding, ct);
            // Teams is PCM16/16 kHz; the shared Realtime WebSocket PCM contract is PCM16/24 kHz.
            var pcm24 = Pcm16SampleRateConverter.From16KhzTo24Khz(frame.Data.Span);
            await pcmGateway.SendInputAudioAsync(providerSessionId, pcm24, ct);
        }
    }

    private async Task PumpAgentOutputAsync(TeamsMeetingMediaBinding binding, Guid userId,
        string providerSessionId, CancellationToken ct)
    {
        var packetizer = new TeamsPcmPacketizer();
        long audioSequence = 0;
        await foreach (var output in pcmGateway.ReceiveOutputAsync(providerSessionId, ct))
        {
            if (!output.Audio.IsEmpty)
            {
                foreach (var pcm16 in packetizer.Append24Khz(output.Audio.Span))
                {
                    await teamsMedia.SendAudioAsync(new TeamsAudioFrame(binding.VoiceSessionId,
                        Interlocked.Increment(ref audioSequence), output.TimestampUtc,
                        TeamsAudioFormat.Pcm16KMono20Ms, pcm16, false), ct);
                }
                continue;
            }
            if (string.IsNullOrWhiteSpace(output.ProviderEventId) || string.IsNullOrWhiteSpace(output.ProviderEventJson))
                continue;

            var envelope = new RealtimeAgentProviderEvent(providerSessionId, output.ProviderEventId,
                output.Sequence, output.ProviderEventJson);
            RealtimeAgentEvent normalized;
            try { normalized = await eventGateway.NormalizeEventAsync(envelope, ct); }
            catch (RealtimeAgentEventException exception) when (exception.Code == "unsupported_event") { continue; }

            if (normalized.Type == RealtimeAgentEventTypes.ParticipantSpeechStarted &&
                _cancelledSpeechEvents.TryAdd(output.ProviderEventId, 0))
            {
                await pcmGateway.CancelPcmResponseAsync(providerSessionId, normalized.ResponseId, ct);
                await teamsMedia.CancelResponseAsync(binding.VoiceSessionId, ct);
            }

            SalesMeetingRealtimeEventResult? processed;
            using (var scope = scopes.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ISalesMeetingRealtimeService>();
                processed = await service.ProcessEventAsync(binding.CompanyId, userId, binding.MeetingSessionId,
                    new SubmitSalesMeetingRealtimeEventRequest(binding.VoiceSessionId, output.ProviderEventId,
                        output.Sequence, output.ProviderEventJson), "teams-media-bridge", ct);
            }
            if (processed?.ToolResultJson is not null && normalized.ToolCallId is not null)
            {
                var result = JsonNode.Parse(processed.ToolResultJson);
                var toolOutput = new JsonObject
                {
                    ["event_id"] = $"evt_{Guid.NewGuid():N}",
                    ["type"] = "conversation.item.create",
                    ["item"] = new JsonObject
                    {
                        ["type"] = "function_call_output",
                        ["call_id"] = normalized.ToolCallId,
                        ["output"] = result?.ToJsonString() ?? "{}"
                    }
                };
                await pcmGateway.SendClientEventAsync(providerSessionId, toolOutput.ToJsonString(), ct);
                await pcmGateway.SendClientEventAsync(providerSessionId,
                    JsonSerializer.Serialize(new { event_id = $"evt_{Guid.NewGuid():N}", type = "response.create" }), ct);
            }
        }
    }

    private async Task<Guid> LoadIdentityAsync(TeamsMeetingMediaBinding binding, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var voice = await db.SalesMeetingVoiceSessions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == binding.CompanyId && x.Id == binding.VoiceSessionId &&
            x.MeetingSessionId == binding.MeetingSessionId && x.AgentId == binding.AgentId, ct);
        if (voice is null) throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.BindingRejected,
            "The durable voice-session identity could not be proved.");
        return voice.StartedByUserId;
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }
}

internal static class Pcm16SampleRateConverter
{
    public static byte[] From16KhzTo24Khz(ReadOnlySpan<byte> source)
    {
        Validate(source);
        var samples = source.Length / 2;
        var destination = new byte[samples * 3]; // 1.5x samples, two bytes each.
        for (var output = 0; output < destination.Length / 2; output++)
        {
            var position = output * 2d / 3d;
            var lower = Math.Min((int)position, samples - 1);
            var upper = Math.Min(lower + 1, samples - 1);
            var fraction = position - lower;
            var a = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(lower * 2, 2));
            var b = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(upper * 2, 2));
            var value = (short)Math.Clamp(Math.Round(a + (b - a) * fraction), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(destination.AsSpan(output * 2, 2), value);
        }
        return destination;
    }

    public static byte[] From24KhzTo16Khz(ReadOnlySpan<byte> source)
    {
        Validate(source);
        var samples = source.Length / 2;
        var outputSamples = samples * 2 / 3;
        var destination = new byte[outputSamples * 2];
        for (var output = 0; output < outputSamples; output++)
        {
            var sourceIndex = output * 3 / 2;
            source.Slice(sourceIndex * 2, 2).CopyTo(destination.AsSpan(output * 2, 2));
        }
        return destination;
    }

    private static void Validate(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty || source.Length % 2 != 0)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.InvalidFrame,
                "PCM16 audio must contain complete signed 16-bit samples.");
    }
}

internal sealed class TeamsPcmPacketizer
{
    private readonly List<byte> _pending = [];

    public IReadOnlyList<ReadOnlyMemory<byte>> Append24Khz(ReadOnlySpan<byte> source)
    {
        var converted = Pcm16SampleRateConverter.From24KhzTo16Khz(source);
        _pending.AddRange(converted);
        var frames = new List<ReadOnlyMemory<byte>>();
        var frameBytes = TeamsAudioFormat.Pcm16KMono20Ms.ExpectedFrameBytes;
        while (_pending.Count >= frameBytes)
        {
            frames.Add(_pending.GetRange(0, frameBytes).ToArray());
            _pending.RemoveRange(0, frameBytes);
        }
        return frames;
    }
}
