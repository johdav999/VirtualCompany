using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

internal interface ISalesRoomAudioSink : IDisposable
{
    Task WriteAsync(int sampleRate, ReadOnlyMemory<short> samples, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
    void Clear();
}

// Serializes the provider queue with cancellation; never accumulates producer tasks.
internal sealed class SalesRoomAudioOutput(ISalesRoomAudioSink sink) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private CancellationTokenSource turn = new();
    private long generation = 1;
    private bool closed;
    private int flushing;
    public long Generation => Interlocked.Read(ref generation);
    public async Task<bool> SendAsync(long expected, int rate, ReadOnlyMemory<short> samples, CancellationToken ct)
    {
        if (rate is not (16000 or 24000 or 48000) || samples.Length != rate / 50)
            throw new SalesRoomMediaException("invalid_pcm_frame");
        if (!await gate.WaitAsync(0, ct)) return false;
        try
        {
            CancellationToken turnToken;
            lock (sync)
            {
                if (closed || flushing != 0 || expected != generation) return false;
                turnToken = turn.Token;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, turnToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { await sink.WriteAsync(rate, samples, timeout.Token); }
            catch (OperationCanceledException) { sink.Clear(); return false; }
            catch { sink.Clear(); throw new SalesRoomMediaException("audio_publication_failed"); }
            return expected == Generation;
        }
        finally { gate.Release(); }
    }
    public async Task<bool> CompleteAsync(long expected, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            CancellationToken turnToken;
            lock (sync)
            {
                if (closed || flushing != 0 || expected != generation) return false;
                turnToken = turn.Token;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, turnToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { await sink.CompleteAsync(timeout.Token); }
            catch (OperationCanceledException) { sink.Clear(); return false; }
            catch { sink.Clear(); throw new SalesRoomMediaException("audio_drain_failed"); }
            return expected == Generation;
        }
        finally { gate.Release(); }
    }
    public async Task<long> CancelAsync()
    {
        CancellationTokenSource old;
        long next;
        lock (sync)
        {
            if (closed) return generation;
            flushing++;
            next = Interlocked.Increment(ref generation);
            old = turn; turn = new CancellationTokenSource(); old.Cancel();
        }
        await gate.WaitAsync();
        try { lock (sync) { if (!closed) sink.Clear(); } }
        finally { lock (sync) flushing--; gate.Release(); old.Dispose(); }
        return next;
    }
    public async ValueTask DisposeAsync()
    {
        lock (sync) { if (closed) return; closed = true; Interlocked.Increment(ref generation); turn.Cancel(); }
        await gate.WaitAsync();
        try { try { sink.Clear(); } finally { sink.Dispose(); } }
        finally { gate.Release(); turn.Dispose(); }
    }
}

internal sealed class LiveKitSalesRoomAudioSink : ISalesRoomAudioSink
{
    public LiveKit.Rtc.AudioSource Source { get; } = new(48000, 1, 100);
    private LiveKit.Rtc.AudioResampler? resampler;
    private int previousRate;
    public async Task WriteAsync(int sampleRate, ReadOnlyMemory<short> samples, CancellationToken cancellationToken)
    {
        if (previousRate != sampleRate)
        {
            resampler?.Dispose(); resampler = sampleRate == 48000 ? null : new((uint)sampleRate, 48000);
            previousRate = sampleRate;
        }
        var frame = new LiveKit.Rtc.AudioFrame(samples.ToArray(), sampleRate, 1, samples.Length);
        if (resampler == null) await Source.CaptureFrameAsync(frame, cancellationToken);
        else foreach (var converted in resampler.Push(frame)) await Source.CaptureFrameAsync(converted, cancellationToken);
    }
    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        if (resampler != null)
        {
            foreach (var frame in resampler.Flush()) await Source.CaptureFrameAsync(frame, cancellationToken);
            resampler.Dispose(); resampler = null; previousRate = 0;
        }
        await Source.WaitForPlayoutAsync(cancellationToken);
    }
    public void Clear() { Source.ClearQueue(); resampler?.Dispose(); resampler = null; previousRate = 0; }
    public void Dispose() { resampler?.Dispose(); Source.Dispose(); }
}
