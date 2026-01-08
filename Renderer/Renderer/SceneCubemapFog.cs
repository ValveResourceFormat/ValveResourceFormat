namespace ValveResourceFormat.Renderer;

#nullable disable

public class SceneCubemapFog(Scene scene) : SceneNode(scene)
{
    public float StartDist { get; set; }
    public float EndDist { get; set; }
    public float FalloffExponent { get; set; }
    public float HeightStart { get; set; }
    public float HeightEnd { get; set; }
    public float HeightExponent { get; set; }
    public float LodBias { get; set; }
    public float Opacity { get; set; }
    public bool UseHeightFog { get; set; }
    public RenderTexture CubemapFogTexture { get; set; }
    public float ExposureBias { get; set; }

    public Vector4 OffsetScaleBiasExponent()
    {
        var scale = 1f / (EndDist - StartDist);
        var offset = -(StartDist * scale);

        return new Vector4(offset, scale, LodBias, FalloffExponent);
    }

    // HeightWidth is equal to HeightEnd - HeightStart
    // Height width ADDS to heightStart
    public Vector4 Height_OffsetScaleExponentLog2Mip()
    {
        var offset = 1f;
        var scale = 0.000001f;
        var exponent = 0f;

        if (HeightEnd - HeightStart > 0) // width = 0 is a substitution for UseHeightFog
        {
            scale = 1f / (HeightStart - HeightEnd);
            offset = 1f - (HeightStart * scale);
            exponent = HeightExponent;
        }

        var value = new Vector4(offset, scale, exponent, Math.Min(7f, CubemapFogTexture.NumMipLevels)); // these latter two values are wrong on deskjob?

        return value;
    }

    public Vector4 CullingParams_Opacity()
    {
        var distCull = StartDist * StartDist;
        var heightCull = (UseHeightFog || ((HeightEnd - HeightStart) > 0)) ? HeightStart : float.PositiveInfinity;

        return new Vector4(distCull, heightCull, MathF.Pow(2f, ExposureBias), Opacity);
    }
}
