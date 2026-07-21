using System.Buffers;

namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Sums multiple sample providers into one stream. Providers that run out of samples are removed automatically.
/// Thread safe: providers can be added and removed while the mixing thread is reading.
/// </summary>
public class SampleProviderMulti : AudioSampleProvider
{
    private readonly LinkedList<IAudioSampleProvider> providers;

    /// <summary>Creates an empty mixer.</summary>
    public SampleProviderMulti()
    {
        providers = new();
    }

    /// <summary>Adds a provider to the mix.</summary>
    public void AddProvider(IAudioSampleProvider provider)
    {
        lock (providers)
        {
            // Idempotent: providers get auto-removed when they run dry and re-added when they
            // resume (e.g. retriggered child events), which must not create duplicates
            providers.Remove(provider);
            providers.AddLast(provider);
        }
    }

    /// <summary>Removes a provider from the mix.</summary>
    public void RemoveProvider(IAudioSampleProvider provider)
    {
        lock (providers)
        {
            providers.Remove(provider);
        }
    }

    private SoundEventCurve? fadeCurve;
    private float fadeDuration;
    private int fadeSampleRate;
    private double fadeElapsedFrames = -1; // < 0 means not fading

    /// <summary>
    /// Starts fading the mix out: subsequent reads ramp the volume down along <paramref name="curve"/>
    /// (or linearly over <paramref name="fallbackSeconds"/> when null) and the mix ends when the fade completes.
    /// </summary>
    public void BeginFadeOut(SoundEventCurve? curve, float fallbackSeconds, int sampleRate)
    {
        lock (providers)
        {
            if (fadeElapsedFrames >= 0)
            {
                return;
            }

            fadeCurve = curve;
            fadeDuration = curve?.MaxX > 0f ? curve.MaxX : fallbackSeconds;
            fadeSampleRate = sampleRate;
            fadeElapsedFrames = 0;
        }
    }

    private float EvaluateFade(double seconds)
    {
        if (fadeCurve != null)
        {
            return Math.Max(fadeCurve.Evaluate((float)seconds), 0f);
        }

        return Math.Clamp(1f - (float)(seconds / fadeDuration), 0f, 1f);
    }

    /// <summary>Removes all providers from the mix.</summary>
    public void ClearProviders()
    {
        lock (providers)
        {
            providers.Clear();
        }
    }

    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        var maxRead = 0;

        lock (providers)
        {
            if (providers.Count > 0)
            {
                var readBuffer = ArrayPool<float>.Shared.Rent(count);

                try
                {
                    var node = providers.First;
                    while (node != null)
                    {
                        var next = node.Next;

                        var read = node.Value.Read(readBuffer, 0, count);
                        var index = offset;
                        for (var i = 0; i < read; i++)
                        {
                            if (i >= maxRead)
                            {
                                buffer[index++] = readBuffer[i];
                            }
                            else
                            {
                                buffer[index++] += readBuffer[i];
                            }
                        }

                        if (read < count && node.List == providers)
                        {
                            providers.Remove(node);
                        }

                        maxRead = Math.Max(maxRead, read);

                        node = next;
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(readBuffer);
                }
            }

            if (fadeElapsedFrames >= 0 && maxRead > 0)
            {
                // Interleaved stereo: two samples per frame
                var startSeconds = fadeElapsedFrames / fadeSampleRate;
                var endSeconds = (fadeElapsedFrames + maxRead / 2.0) / fadeSampleRate;
                var startGain = EvaluateFade(startSeconds);
                var endGain = EvaluateFade(endSeconds);

                // The last sample must land exactly on endGain so consecutive chunks join without a step
                var lastIndex = Math.Max(maxRead - 1, 1);

                for (var i = 0; i < maxRead; i++)
                {
                    buffer[offset + i] *= float.Lerp(startGain, endGain, (float)i / lastIndex);
                }

                fadeElapsedFrames += maxRead / 2.0;

                if (endSeconds >= fadeDuration)
                {
                    // Fade finished: drop everything so the next read comes up empty and fires Over
                    providers.Clear();
                }
            }
        }

        if (maxRead < count)
        {
            // Fires for an empty mix too (e.g. every child event waiting on its retrigger),
            // so owners mark themselves silent and get re-added to their mixer when samples resume
            Over();
        }

        return maxRead;
    }
}
