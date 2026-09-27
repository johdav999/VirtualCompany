namespace VirtualCompany.Application.Agents;

/// <summary>A session-local, streaming speech classifier. Instances must not be shared between audio tracks.</summary>
public interface ISpeechFrameClassifier : IDisposable
{
    bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate);
}

public interface ISpeechFrameClassifierFactory
{
    ISpeechFrameClassifier Create();
}

/// <summary>Deterministic speech-onset confirmation; end-of-turn remains the transport's responsibility.</summary>
public sealed class SpeechOnsetPolicy(int minimumSpeechMilliseconds)
{
    private int consecutiveSpeechMilliseconds;
    private bool confirmed;

    public bool Observe(bool classifiedSpeech, int frameMilliseconds)
    {
        if (frameMilliseconds is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(frameMilliseconds));
        if (!classifiedSpeech)
        {
            consecutiveSpeechMilliseconds = 0;
            return false;
        }
        if (confirmed) return false;
        consecutiveSpeechMilliseconds = Math.Min(1_000, consecutiveSpeechMilliseconds + frameMilliseconds);
        if (consecutiveSpeechMilliseconds < minimumSpeechMilliseconds) return false;
        confirmed = true;
        return true;
    }

    public void Reset() { consecutiveSpeechMilliseconds = 0; confirmed = false; }
}
