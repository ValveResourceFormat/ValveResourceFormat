namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Renders the direction cues that per-ear gains physically cannot: how far above or below the listener a
/// sound is, and whether it is in front of them or behind.
/// </summary>
internal sealed class SpectralCueFilters
{
    /// <summary>
    /// Frequency of the outer ear's first notch, in hertz, at <see cref="NotchElevations"/> degrees of
    /// elevation.
    /// </summary>
    private static readonly float[] NotchFrequencies = [6977f, 7020f, 7278f, 7623f, 7773f, 9647f, 11671f, 11477f];

    /// <summary>
    /// Elevations the <see cref="NotchFrequencies"/> were measured at. It stops at 60 degrees because the
    /// notch stops being a notch above that - straight overhead the deepest feature in the band is only
    /// 6 dB down and no longer follows the sweep, so past here the cue is held rather than extrapolated.
    /// </summary>
    private static readonly float[] NotchElevations = [-45f, -30f, -15f, 0f, 15f, 30f, 45f, 60f];

    /// <summary>Where the notch sits for a sound at ear level, which the peak compensates back to flat.</summary>
    private const float ReferenceNotchHz = 7623f;

    /// <summary>How deep the measured notch is, near enough - the set has it between 14 and 22 dB.</summary>
    private const float NotchDepthDecibels = 18f;

    /// <summary>How sharp the notch is. Wider than this and it cancels against the reference peak, narrower and it stops being audible on anything but noise.</summary>
    private const float NotchQ = 4f;

    /// <summary>
    /// Elevation at which the cue reaches full strength. Below it the notch has barely moved off the
    /// reference anyway, so it is faded in rather than switched on.
    /// </summary>
    private const float FullCueElevation = 45f;

    /// <summary>Corner of the rear shelf, below which the measurements show front and back are the same sound.</summary>
    private const float RearShelfHz = 2000f;

    /// <summary>How much the rear shelf takes off the top, matching the 3 to 4 dB the set measures behind the listener.</summary>
    private const float RearShelfDecibels = -3.5f;

    /// <summary>How quickly the filters follow a moving sound. Long enough not to zipper, short enough to keep up with a sprint.</summary>
    private const float SmoothingSeconds = 0.05f;

    // Below these the filters are doing nothing anyone can hear, and the whole pass is skipped
    private const float MinNotchDecibels = 0.1f;
    private const float MinShelfDecibels = 0.05f;

    // Targets: written on the game thread from the spatializer, read on the mixing thread. Each is a
    // single float, so the mixing thread can only ever see an old value, never half of a new one - which
    // matters, because a biquad handed a torn set of coefficients can go unstable rather than just sound wrong.
    private float targetNotchHz = ReferenceNotchHz;
    private float targetNotchDecibels;
    private float targetShelfDecibels;

    // Smoothed values and the filters themselves: mixing thread only.
    private float notchHz = ReferenceNotchHz;
    private float notchDecibels;
    private float shelfDecibels;

    private Biquad leftDip;
    private Biquad rightDip;
    private Biquad leftPeak;
    private Biquad rightPeak;
    private Biquad leftShelf;
    private Biquad rightShelf;

    private bool active;

    /// <summary>
    /// Points the filters at a sound <paramref name="elevationDegrees"/> above the listener (negative
    /// below) and <paramref name="rearAmount"/> of the way behind them (0 anywhere from abeam forwards).
    /// <paramref name="strength"/> scales the whole effect, 0 disabling it.
    /// </summary>
    public void SetTarget(float elevationDegrees, float rearAmount, float strength)
    {
        if (strength <= 0f)
        {
            targetNotchDecibels = 0f;
            targetShelfDecibels = 0f;
            return;
        }

        var depth = strength * NotchDepthDecibels
            * Math.Min(1f, MathF.Abs(elevationDegrees) / FullCueElevation);

        targetNotchHz = NotchFrequency(elevationDegrees);
        targetNotchDecibels = -depth;
        targetShelfDecibels = RearShelfDecibels * strength * Math.Clamp(rearAmount, 0f, 1f);
    }

    /// <summary>Interpolates <see cref="NotchFrequencies"/>, holding the end values beyond the measured range.</summary>
    private static float NotchFrequency(float elevationDegrees)
    {
        if (elevationDegrees <= NotchElevations[0])
        {
            return NotchFrequencies[0];
        }

        for (var i = 1; i < NotchElevations.Length; i++)
        {
            if (elevationDegrees <= NotchElevations[i])
            {
                var t = (elevationDegrees - NotchElevations[i - 1]) / (NotchElevations[i] - NotchElevations[i - 1]);
                return float.Lerp(NotchFrequencies[i - 1], NotchFrequencies[i], t);
            }
        }

        return NotchFrequencies[^1];
    }

    /// <summary>
    /// Advances the filters towards their target over <paramref name="frames"/> of audio and retunes them,
    /// returning whether there is anything left for <see cref="Process"/> to do. Runs on the mixing thread:
    /// the coefficients are only ever built here, and stepping them by the length of the block that is
    /// about to be filtered is what keeps a sound sweeping overhead sounding the same at any frame rate.
    /// </summary>
    public bool Update(int sampleRate, int frames)
    {
        var elapsed = (float)frames / Math.Max(sampleRate, 1);
        var smoothing = 1f - MathF.Exp(-elapsed / SmoothingSeconds);

        notchHz = float.Lerp(notchHz, targetNotchHz, smoothing);
        notchDecibels = float.Lerp(notchDecibels, targetNotchDecibels, smoothing);
        shelfDecibels = float.Lerp(shelfDecibels, targetShelfDecibels, smoothing);

        var wasActive = active;
        active = MathF.Abs(notchDecibels) > MinNotchDecibels || MathF.Abs(shelfDecibels) > MinShelfDecibels;

        if (!active)
        {
            if (wasActive)
            {
                // Faded to nothing: drop the filter memory rather than leave a tail to come back later
                Reset();
            }

            return false;
        }

        leftDip.SetPeaking(notchHz, NotchQ, notchDecibels, sampleRate);
        rightDip.SetPeaking(notchHz, NotchQ, notchDecibels, sampleRate);

        // The other half of the difference: at ear level this sits on top of the dip and cancels it, and
        // as the sound climbs the two separate into the tilt that reads as height
        leftPeak.SetPeaking(ReferenceNotchHz, NotchQ, -notchDecibels, sampleRate);
        rightPeak.SetPeaking(ReferenceNotchHz, NotchQ, -notchDecibels, sampleRate);

        leftShelf.SetHighShelf(RearShelfHz, shelfDecibels, sampleRate);
        rightShelf.SetHighShelf(RearShelfHz, shelfDecibels, sampleRate);

        return true;
    }

    /// <summary>Filters an interleaved stereo block in place.</summary>
    public void Process(float[] buffer, int offset, int count)
    {
        for (var i = 0; i < count; i += 2)
        {
            var index = offset + i;
            buffer[index] = leftShelf.Process(leftPeak.Process(leftDip.Process(buffer[index])));

            if (i + 1 < count)
            {
                buffer[index + 1] = rightShelf.Process(rightPeak.Process(rightDip.Process(buffer[index + 1])));
            }
        }
    }

    /// <summary>Clears the filter memory for a provider being reused by a new sound somewhere else.</summary>
    public void Reset()
    {
        leftDip.Reset();
        rightDip.Reset();
        leftPeak.Reset();
        rightPeak.Reset();
        leftShelf.Reset();
        rightShelf.Reset();

        notchHz = targetNotchHz;
        notchDecibels = 0f;
        shelfDecibels = 0f;
        active = false;
    }
}
