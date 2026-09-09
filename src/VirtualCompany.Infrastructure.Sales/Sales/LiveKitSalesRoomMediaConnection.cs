using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LiveKit.Rtc;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class LiveKitSalesRoomMediaConnection : ISalesRoomMediaConnection
{
    private readonly Room room = new();
    private readonly ConcurrentDictionary<string, Guid> allowed;
    private readonly Dictionary<string, TrackReader> readers = new();
    private readonly object inputLock = new();
    private readonly HashSet<Task> pumps = new();
    private readonly Channel<SalesRoomAudioFrame> input;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SalesRoomMediaOptions options;
    private SalesRoomAudioOutput? output;
    private LocalAudioTrack? track;
    private Timer? expiry;
    private long received, dropped, sent, trackGeneration;
    private int reconnects, disposing;
    private volatile string state = "connecting";
    private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TrackReader(AudioStream stream, CancellationTokenSource stop, long generation)
    {
        public AudioStream Stream { get; } = stream;
        public CancellationTokenSource Stop { get; } = stop;
        public long Generation { get; } = generation;
        public Task Pump { get; set; } = Task.CompletedTask;
    }
    private LiveKitSalesRoomMediaConnection(IReadOnlyCollection<SalesRoomMediaParticipant> humans, SalesRoomMediaOptions options)
    {
        this.options = options;
        allowed = new(humans.ToDictionary(LiveKitSalesRoomMediaTransport.Identity, p => p.ParticipantId));
        input = Channel.CreateBounded<SalesRoomAudioFrame>(new BoundedChannelOptions(options.MaximumBufferedFrames)
            { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false });
        room.TrackPublished += (_, e) => Subscribe(e.Publication);
        room.TrackSubscribed += (_, e) => StartTrack(e.Publication, e.Track);
        room.TrackUnsubscribed += (_, e) => StopTrack(e.Track.Sid);
        room.ParticipantDisconnected += (_, p) =>
        {
            lock (inputLock)
                foreach (var publication in p.TrackPublications.Values) StopTrack(publication.Sid);
        };
        room.Reconnecting += (_, _) => { state = "reconnecting"; Interlocked.Increment(ref reconnects); _ = StopForConnectionChangeAsync(); };
        room.Reconnected += (_, _) => { if (disposing == 0) state = "paused_after_reconnect"; };
        room.Disconnected += (_, _) => { state = "disconnected"; _ = DisposeAsync().AsTask(); };
    }
    public static async Task<LiveKitSalesRoomMediaConnection> ConnectAsync(SalesRoomMediaToken token,
        IReadOnlyCollection<SalesRoomMediaParticipant> humans, SalesRoomMediaOptions options, CancellationToken ct)
    {
        var connection = new LiveKitSalesRoomMediaConnection(humans, options);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
            await connection.room.ConnectAsync(token.Url, token.Token,
                new RoomOptions { AutoSubscribe = false, JoinRetries = 0 }, timeout.Token);
            var sink = new LiveKitSalesRoomAudioSink();
            connection.output = new SalesRoomAudioOutput(sink);
            connection.track = LocalAudioTrack.Create("sales-agent", sink.Source);
            await connection.room.LocalParticipant!.PublishTrackAsync(connection.track,
                new TrackPublishOptions { Source = LiveKit.Proto.TrackSource.SourceMicrophone }).WaitAsync(timeout.Token);
            foreach (var human in connection.room.RemoteParticipants.Values)
                foreach (var publication in human.TrackPublications.Values.OfType<RemoteTrackPublication>()) connection.Subscribe(publication);
            connection.state = "active";
            connection.expiry = new Timer(_ => { _ = connection.DisposeAsync().AsTask(); }, null,
                TimeSpan.FromMinutes(options.MaximumSessionMinutes), Timeout.InfiniteTimeSpan);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw new SalesRoomMediaException("media_connect_failed", true);
        }
    }
    private void Subscribe(RemoteTrackPublication publication)
    {
        lock (inputLock)
        {
            if (disposing != 0) return;
            publication.SetSubscribed(allowed.ContainsKey(publication.Participant.Identity) &&
                publication.Source == LiveKit.Proto.TrackSource.SourceMicrophone);
        }
    }
    private void StartTrack(RemoteTrackPublication publication, RemoteTrack remoteTrack)
    {
        lock (inputLock)
        {
            if (disposing != 0 || remoteTrack is not RemoteAudioTrack ||
                publication.Source != LiveKit.Proto.TrackSource.SourceMicrophone ||
                !allowed.TryGetValue(publication.Participant.Identity, out var participant)) return;
            if (readers.ContainsKey(remoteTrack.Sid)) return;
            if (readers.Count >= allowed.Count) { publication.SetSubscribed(false); return; }
            var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var reader = new TrackReader(new AudioStream(remoteTrack, sampleRate: 24000, numChannels: 1,
                frameSizeMs: 20, capacity: options.MaximumBufferedFrames), stop, Interlocked.Increment(ref trackGeneration));
            readers[remoteTrack.Sid] = reader;
            reader.Pump = Task.Run(() => PumpAsync(participant, publication.Participant.Identity, remoteTrack.Sid, reader));
            pumps.Add(reader.Pump);
            _ = reader.Pump.ContinueWith(completed => { lock (inputLock) pumps.Remove(completed); }, TaskScheduler.Default);
        }
    }
    private void StopTrack(string sid)
    {
        lock (inputLock)
            if (readers.TryGetValue(sid, out var reader)) reader.Stop.Cancel();
    }
    private async Task PumpAsync(Guid participant, string identity, string sid, TrackReader reader)
    {
        long sequence = 0;
        try
        {
            await foreach (var item in reader.Stream.WithCancellation(reader.Stop.Token))
            {
                var frame = item.Frame;
                if (frame.SampleRate != 24000 || frame.NumChannels != 1 || frame.SamplesPerChannel is < 1 or > 2400)
                { Interlocked.Increment(ref dropped); continue; }
                lock (inputLock)
                {
                    if (!allowed.ContainsKey(identity) || reader.Stop.IsCancellationRequested) break;
                    Interlocked.Increment(ref received);
                    var output = new SalesRoomAudioFrame(participant, sid, reader.Generation, ++sequence,
                        DateTimeOffset.UtcNow, 24000, frame.Data.ToArray());
                    if (!input.Writer.TryWrite(output)) Interlocked.Increment(ref dropped);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { state = "input_degraded"; }
        finally
        {
            reader.Stream.Dispose();
            lock (inputLock)
            {
                if (readers.TryGetValue(sid, out var current) && ReferenceEquals(current, reader)) readers.Remove(sid);
            }
            // Token source is disposed only after the pump is no longer visible to event callbacks.
            reader.Stop.Dispose();
        }
    }
    public async IAsyncEnumerable<SalesRoomAudioFrame> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in input.Reader.ReadAllAsync(cancellationToken))
        {
            bool permitted;
            lock (inputLock) permitted = disposing == 0 && allowed.Values.Contains(frame.ParticipantId) &&
                readers.TryGetValue(frame.TrackId, out var current) && current.Generation == frame.TrackGeneration &&
                !current.Stop.IsCancellationRequested;
            if (permitted) yield return frame;
        }
    }
    public async Task<bool> SendAsync(long turnGeneration, int sampleRate, ReadOnlyMemory<short> samples, CancellationToken cancellationToken)
    {
        if (disposing != 0 || state != "active" || output == null) return false;
        var accepted = await output.SendAsync(turnGeneration, sampleRate, samples, cancellationToken);
        if (accepted) Interlocked.Increment(ref sent);
        return accepted;
    }
    public Task<bool> CompleteSpeechAsync(long turnGeneration, CancellationToken cancellationToken) =>
        output?.CompleteAsync(turnGeneration, cancellationToken) ?? Task.FromResult(false);
    public Task<long> CancelSpeechAsync(CancellationToken cancellationToken) => output?.CancelAsync() ?? Task.FromResult(1L);
    private async Task StopForConnectionChangeAsync()
    {
        try { await CancelSpeechAsync(CancellationToken.None); }
        catch { state = "output_degraded"; }
    }
    public Task RevokeInputAsync(Guid participantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (inputLock)
        {
            foreach (var identity in allowed.Where(x => x.Value == participantId).Select(x => x.Key).ToArray())
            {
                allowed.TryRemove(identity, out _);
                foreach (var p in room.RemoteParticipants.Values.Where(p => p.Identity == identity))
                    foreach (var publication in p.TrackPublications.Values.OfType<RemoteTrackPublication>())
                    { StopTrack(publication.Sid); publication.SetSubscribed(false); }
            }
            // Remove all buffered input on revocation; never retain withdrawn speech for later processing.
            while (input.Reader.TryRead(out _)) { }
        }
        return Task.CompletedTask;
    }
    public SalesRoomMediaStatistics GetStatistics() => new(state, Interlocked.Read(ref received),
        Interlocked.Read(ref dropped), Interlocked.Read(ref sent), reconnects, output?.Generation ?? 1);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposing, 1) != 0) { await disposed.Task; return; }
        var failed = false;
        try
        {
            expiry?.Dispose(); lifetime.Cancel();
            try { if (output != null) await output.DisposeAsync(); } catch { failed = true; }
            Task[] pending;
            lock (inputLock) { allowed.Clear(); pending = pumps.ToArray(); }
            try { await Task.WhenAll(pending); } catch { failed = true; }
            try { await room.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(options.RequestTimeoutSeconds)); } catch { failed = true; }
            try { track?.Dispose(); } catch { failed = true; }
            try { room.Dispose(); } catch { failed = true; }
            while (input.Reader.TryRead(out _)) { }
            input.Writer.TryComplete();
        }
        finally { state = failed ? "cleanup_failed" : "stopped"; disposed.TrySetResult(); }
    }
}
