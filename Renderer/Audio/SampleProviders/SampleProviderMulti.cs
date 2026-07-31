namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Sums multiple sample providers into one stream. Providers that run out of samples are removed automatically.
/// Thread safe: providers can be added and removed while the mixing thread is reading.
/// </summary>
public class SampleProviderMulti : AudioSampleProvider
{
    private readonly LinkedList<IAudioSampleProvider> providers;

    // Cached once: passing the method groups to ApplyFade would allocate a delegate on every read
    private readonly Func<double, float> evaluateFade;
    private readonly Func<double, float> evaluateFadeIn;

    /// <summary>Creates an empty mixer.</summary>
    public SampleProviderMulti()
    {
        providers = new();
        evaluateFade = EvaluateFade;
        evaluateFadeIn = EvaluateFadeIn;
    }

    /// <summary>Adds a provider to the mix. Idempotent: providers get auto-removed when they run dry and re-added when they resume (e.g. retriggered child events), which must not create duplicates.</summary>
    public void AddProvider(IAudioSampleProvider provider)
    {
        lock (providers)
        {
            if (provider is AudioSampleProvider owned)
            {
                // Reuse the provider's own node across add/remove cycles instead of allocating one per add
                var node = owned.MixNode ??= new LinkedListNode<IAudioSampleProvider>(provider);

                if (node.List == providers)
                {
                    return;
                }

                if (node.List == null)
                {
                    providers.AddLast(node);
                    return;
                }

                // Attached to a different mix - the fixed provider tree should make this unreachable,
                // fall through to a plain (allocating) add rather than mutate another mix's list unlocked
            }

            providers.Remove(provider);
            providers.AddLast(provider);
        }
    }

    /// <summary>Removes a provider from the mix.</summary>
    public void RemoveProvider(IAudioSampleProvider provider)
    {
        lock (providers)
        {
            if (provider is AudioSampleProvider { MixNode: { } node })
            {
                if (node.List == providers)
                {
                    providers.Remove(node);
                }

                return;
            }

            providers.Remove(provider);
        }
    }

    private SoundEventCurve? fadeCurve;
    private float fadeDuration;
    private int fadeSampleRate;
    private double fadeElapsedFrames = -1; // < 0 means not fading

    private float fadeInDuration;
    private int fadeInSampleRate;
    private double fadeInElapsedFrames = -1; // < 0 means not fading in / already finished

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

    /// <summary>
    /// Starts fading the mix in from silence over <paramref name="seconds"/>, eased rather than linear so
    /// a freshly started event (e.g. entering a soundscape with none previously active) doesn't jump
    /// straight to full volume. Harmless to call more than once; only the first call takes effect.
    /// </summary>
    public void BeginFadeIn(float seconds, int sampleRate)
    {
        lock (providers)
        {
            if (fadeInElapsedFrames >= 0 || seconds <= 0f)
            {
                return;
            }

            fadeInDuration = seconds;
            fadeInSampleRate = sampleRate;
            fadeInElapsedFrames = 0;
        }
    }

    /// <summary>
    /// Ramps <paramref name="count"/> samples along <paramref name="evaluate"/> and advances the fade,
    /// returning whether the fade reached its end during this chunk. No-op for a fade that is not running
    /// (negative <paramref name="elapsedFrames"/>) or an empty read.
    /// </summary>
    private static bool ApplyFade(float[] buffer, int offset, int count, ref double elapsedFrames,
        int sampleRate, float duration, Func<double, float> evaluate)
    {
        if (elapsedFrames < 0 || count <= 0)
        {
            return false;
        }

        // Interleaved stereo: two samples per frame
        var startSeconds = elapsedFrames / sampleRate;
        var endSeconds = (elapsedFrames + count / 2.0) / sampleRate;
        var startGain = evaluate(startSeconds);
        var endGain = evaluate(endSeconds);

        // The last sample must land exactly on endGain so consecutive chunks join without a step
        var lastIndex = Math.Max(count - 1, 1);

        for (var i = 0; i < count; i++)
        {
            buffer[offset + i] *= float.Lerp(startGain, endGain, (float)i / lastIndex);
        }

        elapsedFrames += count / 2.0;
        return endSeconds >= duration;
    }

    private float EvaluateFade(double seconds)
    {
        if (fadeCurve != null)
        {
            return Math.Max(fadeCurve.Evaluate((float)seconds), 0f);
        }

        return Math.Clamp(1f - (float)(seconds / fadeDuration), 0f, 1f);
    }

    private float EvaluateFadeIn(double seconds)
    {
        var t = Math.Clamp((float)(seconds / fadeInDuration), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Removes all providers from the mix.</summary>
    public void ClearProviders()
    {
        lock (providers)
        {
            providers.Clear();
        }
    }

    /// <summary>
    /// Clears any fade-out/fade-in in progress, so a pooled sound event reused for a new play
    /// does not start silent (or mid-fade) because its previous play ended in a fade.
    /// </summary>
    public void ResetFades()
    {
        lock (providers)
        {
            fadeCurve = null;
            fadeElapsedFrames = -1;
            fadeInElapsedFrames = -1;
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
                var readBuffer = MixScratch.Push(count);

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
                    MixScratch.Pop();
                }
            }

            if (ApplyFade(buffer, offset, maxRead, ref fadeElapsedFrames, fadeSampleRate, fadeDuration, evaluateFade))
            {
                // Fade-out finished: drop everything so the next read comes up empty and fires Over
                providers.Clear();
            }

            if (ApplyFade(buffer, offset, maxRead, ref fadeInElapsedFrames, fadeInSampleRate, fadeInDuration, evaluateFadeIn))
            {
                // Fade-in finished: stop applying it, unlike the fade-out this never touches providers
                fadeInElapsedFrames = -1;
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
