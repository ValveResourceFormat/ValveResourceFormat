namespace ValveResourceFormat.ResourceTypes.Choreo.Enums
{
    /// <summary>
    /// How a choreography curve interpolates towards or away from a sample.
    /// </summary>
    public enum CurveInterpolator : byte
    {
#pragma warning disable CS1591
        Default = 0,
        CatmullRomNormalizeX = 1,
        EaseIn = 2,
        EaseOut = 3,
        EaseInOut = 4,
        BSpline = 5,
        Linear = 6,
        Kochanek = 7,
        KochanekEarly = 8,
        KochanekLate = 9,
        SimpleCubic = 10,
        CatmullRom = 11,
        CatmullRomNormalize = 12,
        CatmullRomTangent = 13,
        ExponentialDecay = 14,
        Hold = 15,
        Bezier = 16,
#pragma warning restore CS1591
    }
}
