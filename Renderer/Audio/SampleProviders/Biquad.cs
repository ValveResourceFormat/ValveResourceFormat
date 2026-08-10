namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// A second order IIR section, in transposed direct form II so its state stays well behaved when the
/// coefficients are retuned underneath it (which happens continuously as a sound moves around the listener).
/// Coefficients follow the standard audio EQ cookbook.
/// </summary>
internal struct Biquad
{
    private float b0;
    private float b1;
    private float b2;
    private float a1;
    private float a2;

    private float z1;
    private float z2;

    /// <summary>Gets whether this section is passing its input through untouched.</summary>
    public readonly bool IsBypassed => b0 == 1f && b1 == 0f && b2 == 0f && a1 == 0f && a2 == 0f;

    /// <summary>Passes the signal through unchanged, without disturbing the state of a section that was already flat.</summary>
    public void SetBypass()
    {
        (b0, b1, b2, a1, a2) = (1f, 0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Tunes this section to a peak or a dip of <paramref name="gainDecibels"/> centered on
    /// <paramref name="frequency"/>, <paramref name="q"/> setting how narrow it is.
    /// </summary>
    public void SetPeaking(float frequency, float q, float gainDecibels, int sampleRate)
    {
        // Above Nyquist there is no filter to build, and the cookbook's tan() blows up approaching it
        if (gainDecibels == 0f || frequency <= 0f || frequency >= sampleRate * 0.49f)
        {
            SetBypass();
            return;
        }

        var a = MathF.Pow(10f, gainDecibels / 40f);
        var w0 = 2f * MathF.PI * frequency / sampleRate;
        var cosW0 = MathF.Cos(w0);
        var alpha = MathF.Sin(w0) / (2f * q);

        Normalize(
            1f + (alpha * a), -2f * cosW0, 1f - (alpha * a),
            1f + (alpha / a), -2f * cosW0, 1f - (alpha / a));
    }

    /// <summary>
    /// Tunes this section to a shelf applying <paramref name="gainDecibels"/> above
    /// <paramref name="frequency"/> and leaving everything below it alone.
    /// </summary>
    public void SetHighShelf(float frequency, float gainDecibels, int sampleRate)
    {
        if (gainDecibels == 0f || frequency <= 0f || frequency >= sampleRate * 0.49f)
        {
            SetBypass();
            return;
        }

        var a = MathF.Pow(10f, gainDecibels / 40f);
        var w0 = 2f * MathF.PI * frequency / sampleRate;
        var cosW0 = MathF.Cos(w0);
        var alpha = MathF.Sin(w0) * 0.5f * MathF.Sqrt(2f);
        var twoSqrtAAlpha = 2f * MathF.Sqrt(a) * alpha;

        Normalize(
            a * (a + 1f + ((a - 1f) * cosW0) + twoSqrtAAlpha),
            -2f * a * (a - 1f + ((a + 1f) * cosW0)),
            a * (a + 1f + ((a - 1f) * cosW0) - twoSqrtAAlpha),
            a + 1f - ((a - 1f) * cosW0) + twoSqrtAAlpha,
            2f * (a - 1f - ((a + 1f) * cosW0)),
            a + 1f - ((a - 1f) * cosW0) - twoSqrtAAlpha);
    }

    private void Normalize(float nb0, float nb1, float nb2, float na0, float na1, float na2)
    {
        var scale = 1f / na0;

        b0 = nb0 * scale;
        b1 = nb1 * scale;
        b2 = nb2 * scale;
        a1 = na1 * scale;
        a2 = na2 * scale;
    }

    /// <summary>Filters one sample.</summary>
    public float Process(float x)
    {
        var y = (b0 * x) + z1;

        z1 = (b1 * x) - (a1 * y) + z2;
        z2 = (b2 * x) - (a2 * y);

        return y;
    }

    /// <summary>Clears the filter memory, so a reused provider does not start on the tail of the last sound.</summary>
    public void Reset()
    {
        z1 = 0f;
        z2 = 0f;
    }
}
