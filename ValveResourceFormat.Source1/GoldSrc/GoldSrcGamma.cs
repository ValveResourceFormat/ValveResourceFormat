namespace ValveResourceFormat.GoldSrc;

/// <summary>The gamma curves the engine shades with, as exponents that turn a value into screen gamma.</summary>
internal static class GoldSrcGamma
{
    private const float TextureGamma = 2f;
    private const float LightGamma = 2.5f;
    private const float ScreenGamma = 2.2f;

    /// <summary>The exponent of the curve of the palette of every texture.</summary>
    public const float TextureExponent = TextureGamma / ScreenGamma;

    /// <summary>The exponent of the curve of light before it multiplies a texture.</summary>
    public const float LightExponent = LightGamma / ScreenGamma;
}
