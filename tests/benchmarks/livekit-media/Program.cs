using System.Diagnostics;
using System.Text.Json;
using LiveKit.Rtc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

// External integration executable. No HTTP endpoints, customer content or logged tokens.
if (args.Length == 0 || args[0] == "--probe")
{
    try
    {
        using var source = new AudioSource(48000, 1, 100);
        using var resampler = new AudioResampler(24000, 48000);
        var frames = new List<AudioFrame>();
        for (var i = 0; i < 10; i++) frames.AddRange(resampler.Push(new AudioFrame(new short[480], 24000, 1, 480)));
        frames.AddRange(resampler.Flush());
        if (frames.Sum(f => f.SamplesPerChannel) < 9000) throw new InvalidOperationException("Resampling produced insufficient output.");
        foreach (var frame in frames) await source.CaptureFrameAsync(frame);
        source.ClearQueue();
        Console.WriteLine(JsonSerializer.Serialize(new { native_probe = "passed", runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            rtc = typeof(Room).Assembly.GetName().Version?.ToString(), resampled_frames = frames.Count, live_media = "not_run" }));
        return 0;
    }
    catch (Exception e)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { native_probe = "failed", error_type = e.GetType().Name }));
        return 1;
    }
}
if (args[0] != "--live" || args.Length != 2 || !int.TryParse(args[1], out var minutes) || minutes is < 1 or > 60)
{
    Console.Error.WriteLine("Usage: --probe OR --live <1..60 minutes>. Live mode creates a disposable room in the configured isolated project.");
    return 2;
}
var options = new SalesRoomMediaOptions
{
    Enabled = true, Url = Environment.GetEnvironmentVariable("LIVEKIT_URL") ?? "",
    ApiKey = Environment.GetEnvironmentVariable("LIVEKIT_API_KEY") ?? "",
    ApiSecret = Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET") ?? "",
    MaximumSessionMinutes = 60
};
if (options.ConfigurationProblem != null)
{
    Console.Error.WriteLine("Live test blocked: configure LIVEKIT_URL, LIVEKIT_API_KEY and LIVEKIT_API_SECRET for an isolated project.");
    return 2;
}
var services = new ServiceCollection(); services.AddHttpClient("SalesBrowserRoom.LiveKit");
using var provider = services.BuildServiceProvider();
var transport = new LiveKitSalesRoomMediaTransport(Options.Create(options), provider.GetRequiredService<IHttpClientFactory>());
var scope = new SalesRoomMediaScope(Guid.NewGuid(), Guid.NewGuid());
var humanIds = new[] { new SalesRoomMediaParticipant(Guid.NewGuid(), false), new SalesRoomMediaParticipant(Guid.NewGuid(), false) };
var agent = new SalesRoomMediaParticipant(Guid.NewGuid(), true);
var rooms = new List<Room>(); var sources = new List<AudioSource>(); var tracks = new List<LocalAudioTrack>();
var received = new System.Collections.Concurrent.ConcurrentDictionary<Guid, long>();
using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(minutes).Add(TimeSpan.FromSeconds(30)));
ISalesRoomMediaConnection? connection = null;
Task? receive = null; bool passed = false; bool cleanup = true;
var watches = new List<Task>(); var audienceFrames = new long[2];
var initialMemory = Process.GetCurrentProcess().WorkingSet64;
try
{
    await transport.EnsureRoomAsync(scope, Guid.NewGuid(), 7, stop.Token);
    for (var i = 0; i < 2; i++)
    {
        var index = i;
        var token = transport.IssueToken(scope, humanIds[i]);
        var room = new Room(); rooms.Add(room);
        room.TrackSubscribed += (_, e) =>
        {
            if (e.Track is not RemoteAudioTrack || e.Participant.Identity != LiveKitSalesRoomMediaTransport.Identity(agent)) return;
            lock (watches) watches.Add(Task.Run(async () =>
            {
                using var stream = new AudioStream(e.Track, capacity: 25);
                try { await foreach (var frame in stream.WithCancellation(stop.Token)) Interlocked.Increment(ref audienceFrames[index]); }
                catch (OperationCanceledException) { }
            }));
        };
        await room.ConnectAsync(token.Url, token.Token, cancellationToken: stop.Token);
        var source = new AudioSource(48000, 1, 100); sources.Add(source);
        var track = LocalAudioTrack.Create("human-microphone", source); tracks.Add(track);
        await room.LocalParticipant!.PublishTrackAsync(track, new TrackPublishOptions { Source = LiveKit.Proto.TrackSource.SourceMicrophone });
    }
    connection = await transport.ConnectAgentAsync(scope, agent, humanIds, stop.Token);
    receive = Task.Run(async () =>
    {
        try { await foreach (var frame in connection.ReceiveAsync(stop.Token)) received.AddOrUpdate(frame.ParticipantId, 1, (_, n) => n + 1); }
        catch (OperationCanceledException) { }
    });
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
    var deadline = DateTimeOffset.UtcNow.AddMinutes(minutes);
    var generation = connection.GetStatistics().TurnGeneration;
    while (DateTimeOffset.UtcNow < deadline && await timer.WaitForNextTickAsync(stop.Token))
    {
        for (var i = 0; i < 2; i++)
        {
            var pcm = Enumerable.Range(0, 960).Select(n => (short)(1500 * Math.Sin(2 * Math.PI * (440 + i * 220) * n / 48000))).ToArray();
            await sources[i].CaptureFrameAsync(new AudioFrame(pcm, 48000, 1, 960), stop.Token);
        }
        await connection.SendAsync(generation, 24000, Enumerable.Range(0, 480).Select(n => (short)(1500 * Math.Sin(2 * Math.PI * 330 * n / 24000))).ToArray(), stop.Token);
    }
    await connection.CompleteSpeechAsync(generation, stop.Token);
    var next = await connection.CancelSpeechAsync(stop.Token);
    var lateRejected = !await connection.SendAsync(generation, 24000, new short[480], stop.Token);
    await transport.RemoveParticipantAsync(scope, humanIds[1], stop.Token);
    passed = next > generation && lateRejected && humanIds.All(p => received.GetValueOrDefault(p.ParticipantId) > 0) && audienceFrames.All(n => n > 0);
    Console.WriteLine(JsonSerializer.Serialize(new { duration_minutes = minutes, passed, distinct_inputs = received.Count,
        audience_frames = audienceFrames, lateRejected, statistics = connection.GetStatistics(),
        memory_start = initialMemory, memory_end = Process.GetCurrentProcess().WorkingSet64,
        browser_microphones = "not_run", azure_runtime = "operator_must_identify", audible_cancellation = "not_measured" }));
}
catch (Exception e)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { failed = true, error_type = e.GetType().Name,
        code = (e as SalesRoomMediaException)?.Code }));
}
finally
{
    stop.Cancel();
    if (connection != null) { await connection.DisposeAsync(); cleanup &= connection.GetStatistics().State == "stopped"; }
    if (receive != null) try { await receive; } catch { cleanup = false; }
    foreach (var room in rooms) try { await room.DisconnectAsync(); room.Dispose(); } catch { cleanup = false; }
    Task[] pending; lock (watches) pending = watches.ToArray();
    try { await Task.WhenAll(pending); } catch { cleanup = false; }
    foreach (var track in tracks) track.Dispose();
    foreach (var source in sources) source.Dispose();
    try { await transport.DeleteRoomAsync(scope, CancellationToken.None); } catch { cleanup = false; }
    Console.WriteLine(JsonSerializer.Serialize(new { cleanup }));
}
return passed && cleanup ? 0 : 1;
