using System;
using System.Numerics;

namespace GUI.Types.Renderer;
class SceneCubemapFog : SceneNode
{
    public float StartDist { get; set; }
    public float EndDist { get; set; }
    public float FalloffExponent { get; set; }
    public float HeightStart { get; set; }
    public float HeightWidth { get; set; }
    public float HeightExponent { get; set; }
    public float LodBias { get; set; }
    public RenderTexture CubemapFogTexture { get; set; }

    public Vector4 OffsetScaleBiasExponent(Vector3 mapOffset, float mapScale)
    {
        var scale = mapScale / (EndDist - StartDist);
        var offset = -(StartDist * scale) / mapScale;

        //Console.WriteLine($"depth fog bias scale {new Vector2(offset, scale)}");
        return new Vector4(offset, scale, LodBias, FalloffExponent);
    }
    public Vector4 Height_OffsetScaleExponentLog2Mip(Vector3 mapOffset, float mapScale)
    {
        float offset;
        float scale;
        if (HeightWidth > 0)
        {
            Console.WriteLine($"{mapOffset}");
            var start = (HeightStart - mapOffset.Z) / mapScale;

            scale = mapScale / HeightWidth; // indeed applies to scale and start
            offset = -(start * scale);

            //Console.WriteLine($"height cubefog {new Vector4(offset, scale, HeightExponent, CubemapFogTexture.NumMipLevels)}");
        }
        else
        {
            offset = 0f;
            scale = 0f;
        }

        return new Vector4(offset, scale, HeightExponent, Math.Min(7f, CubemapFogTexture.NumMipLevels));
    }
    public Vector2 CullingParams(Vector3 mapOffset, float mapScale)
    {
        var distCull = StartDist / mapScale;
        distCull *= distCull;
        var heightCull = (HeightWidth > 0) ? ((HeightStart - mapOffset.Z) / mapScale) : float.PositiveInfinity;
        return new Vector2(distCull, heightCull);
    }

    public SceneCubemapFog(Scene scene) : base(scene)
    {
    }
    public override void Render(Scene.RenderContext context)
    {
    }

    public override void Update(Scene.UpdateContext context)
    {
    }
}
