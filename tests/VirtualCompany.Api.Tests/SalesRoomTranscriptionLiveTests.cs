using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Platform;
using VirtualCompany.Infrastructure.Sales;
using Xunit.Abstractions;

namespace VirtualCompany.Api.Tests;

// Opt-in: two bounded provider sessions, synthetic speech only, no microphone capture or retries.
public sealed class SalesRoomTranscriptionLiveTests(ITestOutputHelper output)
{
    [RoomTranscriptionLiveFact]
    [Trait("Category", "External")]
    public async Task Finance_question_survives_local_segmentation_and_provider_transcription()
    {
        var original = ReadPcm(Environment.GetEnvironmentVariable("VC_ROOM_TRANSCRIPTION_WAV")!);
        var segmenter = new SalesRoomVoiceActivitySegmenter(new SalesRoomAgentOptions(),
            new SpeechAwareLocalVoiceActivityDetector(new WebRtcSpeechFrameClassifierFactory()));
        var participant = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var padded = original.Concat(new short[24_000]).ToArray();
        var utterances = new List<SalesRoomDetectedUtterance>();
        for (var offset = 0; offset + 480 <= padded.Length; offset += 480)
        {
            var result = segmenter.Push(new(participant, "synthetic", 1, offset / 480,
                now.AddMilliseconds(offset / 24), 24_000, padded.AsMemory(offset, 480)));
            if (result.Utterance is { } utterance) utterances.Add(utterance);
        }
        utterances.AddRange(segmenter.FlushExpired(now.AddMinutes(1)));
        segmenter.ClearAll();
        Assert.Single(utterances);
        output.WriteLine($"Original {original.Length / 24} ms; segmented {utterances[0].Samples.Length / 24} ms.");
        var results = new List<string>();
        foreach (var audio in new[] { original, utterances[0].Samples.ToArray() })
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var gateway = new OpenAiRealtimeAgentSessionGateway(new Clients(), Options.Create(new SharedRealtimeAgentOptions { Enabled = true }),
                NullLogger<OpenAiRealtimeAgentSessionGateway>.Instance);
            var session = await gateway.CreatePcmSessionAsync(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "sales_browser_room_segmented_transcription", "Transcribe speech accurately. Do not answer or speak.", [],
                TimeSpan.FromMinutes(1), "synthetic-finance-question", true), stop.Token);
            try
            {
                var received = Receive();
                for (var offset = 0; offset < audio.Length; offset += 48_000)
                    await gateway.SendInputAudioAsync(session.ProviderSessionId,
                        MemoryMarshal.AsBytes(audio.AsSpan(offset, Math.Min(48_000, audio.Length - offset))).ToArray(), stop.Token);
                await gateway.SendClientEventAsync(session.ProviderSessionId, "{\"type\":\"input_audio_buffer.commit\"}", stop.Token);
                var text = await received;
                output.WriteLine($"Synthetic transcript ({results.Count}): {text}"); results.Add(text);

                async Task<string> Receive()
                {
                    await foreach (var evt in gateway.ReceiveOutputAsync(session.ProviderSessionId, stop.Token))
                    {
                        if (evt.ProviderEventJson is null) continue;
                        using var doc = JsonDocument.Parse(evt.ProviderEventJson);
                        var root = doc.RootElement;
                        var type = root.GetProperty("type").GetString();
                        if (type == "error") throw new InvalidOperationException("Provider rejected synthetic replay: " + root.GetProperty("error").GetProperty("code").GetString());
                        if (type == "conversation.item.input_audio_transcription.completed") return root.GetProperty("transcript").GetString() ?? "";
                    }
                    throw new InvalidOperationException("No final synthetic transcript received.");
                }
            }
            finally { await gateway.TerminatePcmSessionAsync(session.ProviderSessionId, CancellationToken.None); }
        }
        Assert.All(results, text =>
        {
            Assert.Contains("finance", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("agent", text, StringComparison.OrdinalIgnoreCase);
            Assert.True(SalesRoomAgentWorker.IsAddressedQuestion(text, "Alex") ||
                SalesRoomAgentWorker.ShouldTreatInterruptedSpeechAsAddressedQuestion(text, true, 1, false),
                "The provider transcript must reach the production question-routing gate.");
        });
    }

    private static short[] ReadPcm(string path)
    {
        using var file = File.OpenRead(path); using var reader = new BinaryReader(file);
        Assert.Equal("RIFF", new string(reader.ReadChars(4))); _ = reader.ReadInt32();
        Assert.Equal("WAVE", new string(reader.ReadChars(4)));
        while (file.Position < file.Length)
        {
            var chunk = new string(reader.ReadChars(4)); var length = reader.ReadInt32();
            if (chunk == "data") return MemoryMarshal.Cast<byte, short>(reader.ReadBytes(length)).ToArray();
            file.Position += length + (length & 1);
        }
        throw new InvalidDataException("Synthetic PCM data missing.");
    }
    private sealed class Clients : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}

public sealed class RoomTranscriptionLiveFactAttribute : FactAttribute
{
    public RoomTranscriptionLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VC_ROOM_TRANSCRIPTION_LIVE") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VC_ROOM_TRANSCRIPTION_WAV")))
            Skip = "Requires an explicit synthetic audio/provider test run.";
    }
}
