using VirtualCompany.Application.Agents;
using WebRtcVad.NET;

namespace VirtualCompany.Infrastructure.Platform;

/// <summary>One adaptive WebRTC GMM detector per microphone track; no native runtime or audio retention.</summary>
internal sealed class WebRtcSpeechFrameClassifierFactory : ISpeechFrameClassifierFactory
{
    public ISpeechFrameClassifier Create() => new WebRtcSpeechFrameClassifier();
}

internal sealed class WebRtcSpeechFrameClassifier : ISpeechFrameClassifier
{
    private readonly global::WebRtcVad.NET.WebRtcVad vad = new()
    {
        SampleRate = SampleRate.Rate48kHz,
        FrameLength = FrameLength.Length10ms,
        OperatingMode = OperatingMode.Aggressive
    };

    public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate)
    {
        if (sampleRate != 24_000 || samples.Length is < 240 or > 2_400 || samples.Length % 240 != 0)
            throw new ArgumentException("Speech classification requires 10-100 ms of 24 kHz PCM16 in 10 ms increments.");

        Span<short> resampled = stackalloc short[480];
        var speech = false;
        for (var offset = 0; offset < samples.Length; offset += 240)
        {
            var chunk = samples.Slice(offset, 240);
            for (var i = 0; i < chunk.Length; i++)
            {
                resampled[2 * i] = chunk[i];
                resampled[2 * i + 1] = (short)((chunk[i] + (i == chunk.Length - 1 ? chunk[i] : chunk[i + 1])) / 2);
            }
            // A steady narrow-band motor hum or test tone can fool the GMM. Require
            // broadband variation as well; isolated clicks are rejected by onset timing.
            speech = (vad.HasSpeech(resampled) && HasBroadbandContent(chunk)) || speech;
        }
        return speech;
    }

    private static bool HasBroadbandContent(ReadOnlySpan<short> samples)
    {
        long energy = 0, curvature = 0;
        for (var i = 2; i < samples.Length; i++)
        {
            var value = (int)samples[i];
            var secondDifference = value - 2 * (int)samples[i - 1] + samples[i - 2];
            energy += (long)value * value;
            curvature += (long)secondDifference * secondDifference;
        }
        return energy > 0 && curvature * 20_000 >= energy;
    }

    public void Dispose() => vad.Dispose();
}
