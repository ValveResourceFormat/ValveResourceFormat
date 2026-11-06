using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Bsp;

internal static partial class Source1SpriteConverter
{
    private const string SpotlightBeamMaterial = "sprites/glow_test02";
    private const string SpotlightHaloMaterial = "sprites/light_glow03";

    private const int SpotlightNoDynamicLightFlag = 2;

    private const float DefaultSpotlightLength = 500f;
    private const float DefaultSpotlightWidth = 10f;
    private const float DefaultBeamSpotlightWidth = 50f;
    private const float DefaultBeamSpotlightColorScale = 0.7f;
    private const float MaxSpotlightWidth = 102.3f;

    // The beam is drawn at a fixed brightness of the color of the entity
    private const float SpotlightBrightness = 64f / 255f;

    // The halo at the start of the beam is a quad this far from its center to each edge, which grows up to twice as much near the beam
    private const float SpotlightHaloSize = 60f;

    // The radius of what has to be in view for the halo to show
    private const float SpotlightHaloProxySize = 8f;

    private const float SpotlightLightWidthScale = 1.8f;
    private const float SpotlightLightRadiusScale = 3f;
    private const float SpotlightLightHeight = 5f;

    private static bool AddSpotlight(
        Source1ConvertedMap map,
        Source1BakedMeshSet<SpriteVertex> meshes,
        IBrushLightingLocator locator,
        Func<string, Source1MaterialInfo?> getMaterialInfo,
        Lazy<BspWorldTrace> trace,
        BspEntity entity)
    {
        if ((entity.GetInt("spawnflags") & StartOnFlag) == 0)
        {
            return false;
        }

        if (getMaterialInfo(SpotlightBeamMaterial) == null || getMaterialInfo(SpotlightHaloMaterial) == null)
        {
            return false;
        }

        var isBeamClass = entity.ClassName == "beam_spotlight";
        var maxLength = entity.GetFloat("SpotlightLength");

        if (maxLength <= 0f)
        {
            maxLength = DefaultSpotlightLength;
        }

        var width = entity.GetFloat("SpotlightWidth");

        if (width <= 0f)
        {
            width = isBeamClass ? DefaultBeamSpotlightWidth : DefaultSpotlightWidth;
        }

        width = MathF.Min(width, MaxSpotlightWidth);

        var colorScale = entity.GetFloat("HDRColorScale", isBeamClass ? DefaultBeamSpotlightColorScale : 1f);
        var start = entity.Origin;
        var direction = EntityTransformHelper.EulerAnglesToForwardDirection(entity.Angles);
        var end = start + (direction * 2f * maxLength);

        if (isBeamClass || entity.GetInt("IgnoreSolid") == 0)
        {
            var hit = trace.Value.Trace(start, end);
            end = Vector3.Lerp(start, end, hit.Fraction);
        }

        var delta = end - start;
        var length = delta.Length();

        if (length < 1f)
        {
            return false;
        }

        var renderColor = GetRenderColor(entity);
        var beamColor = ColorSpace.SrgbGammaToLinear(renderColor / 255f * SpotlightBrightness) * colorScale;
        var haloColor = ColorSpace.SrgbGammaToLinear(renderColor / 255f) * colorScale;
        var inSky = locator.IsInSky(start);

        var beamChunk = meshes.Get(inSky, start, GetMaterialPath(SpotlightBeamMaterial, RenderMode.TransAdd));
        AddSpotlightBeam(beamChunk, start, delta, width, beamColor);

        var haloChunk = meshes.Get(inSky, start, GetMaterialPath(SpotlightHaloMaterial, RenderMode.Glow));
        AddSpotlightHalo(haloChunk, start, delta, width, haloColor);

        if (!isBeamClass)
        {
            AddSpotlightEndLight(map, entity, end, renderColor, width, length, maxLength);
        }

        return true;
    }

    // The color of an entity is white unless it names one
    private static Vector3 GetRenderColor(BspEntity entity)
    {
        if (!entity.Properties.ContainsKey("rendercolor"))
        {
            return new Vector3(255f);
        }

        return Vector3.Clamp(entity.GetVector3("rendercolor"), Vector3.Zero, new Vector3(255f));
    }

    // A ribbon from the start to the end that is as wide on both ends, fades out along its length, and turns around
    // its axis to face the viewer. The vertices at the end are black, which fades the ribbon to nothing there.
    private static void AddSpotlightBeam(Source1BakedMeshSet<SpriteVertex>.Chunk chunk, Vector3 start, Vector3 delta, float width, Vector3 color)
    {
        var reach = width + BoundsPadding;
        var indices = new int[4];

        for (var corner = 0; corner < indices.Length; corner++)
        {
            var isEnd = corner is 1 or 2;
            var isRight = corner < 2;
            var fraction = isEnd ? 1f : 0f;
            var position = start + (delta * fraction);
            var side = isRight ? width : -width;
            var vertexColor = isEnd ? new Vector4(Vector3.Zero, 1f) : new Vector4(color, 1f);

            var vertex = new SpriteVertex
            {
                Position = position,
                TexCoord = new Vector2(isRight ? 0f : 1f, fraction),
                Color = vertexColor,
                Billboard = new Vector3(side, fraction, (float)Orientation.Beam),
                Params = new Vector4(width, 0f, (float)FadeMode.None, 0f),
                Fade = new Vector4(delta, 0f),
            };

            indices[corner] = chunk.AddVertex(vertex, position, reach);
        }

        chunk.AddQuad(indices[0], indices[1], indices[2], indices[3]);
    }

    // The glow at the start of the beam, which the shader shows only to a viewer in front of the light
    private static void AddSpotlightHalo(Source1BakedMeshSet<SpriteVertex>.Chunk chunk, Vector3 start, Vector3 delta, float width, Vector3 color)
    {
        var reach = (SpotlightHaloSize * 2f) + BoundsPadding;
        var indices = new int[4];

        for (var corner = 0; corner < indices.Length; corner++)
        {
            var isRight = corner is 2 or 3;
            var isUp = corner is 1 or 2;
            var offset = new Vector2(isRight ? SpotlightHaloSize : -SpotlightHaloSize, isUp ? SpotlightHaloSize : -SpotlightHaloSize);

            var vertex = new SpriteVertex
            {
                Position = start,
                TexCoord = new Vector2(isRight ? 1f : 0f, isUp ? 0f : 1f),
                Color = new Vector4(color, 1f),
                Billboard = new Vector3(offset, (float)Orientation.BeamHalo),
                Params = new Vector4(width, 0f, (float)FadeMode.None, 0f),
                Fade = new Vector4(delta, SpotlightHaloProxySize),
            };

            indices[corner] = chunk.AddVertex(vertex, start, reach);
        }

        chunk.AddQuad(indices[0], indices[1], indices[2], indices[3]);
    }

    // The end of a spotlight that has a name or a parent is an entity of the game that lights its surroundings while the
    // beam is no longer than its length. The end of one that has neither is removed when the map starts.
    private static void AddSpotlightEndLight(Source1ConvertedMap map, BspEntity entity, Vector3 end, Vector3 renderColor, float width, float length, float maxLength)
    {
        var isKept = entity.TargetName.Length > 0 || entity.Properties.ContainsKey("parentname");
        var hasLight = (entity.GetInt("spawnflags") & SpotlightNoDynamicLightFlag) == 0;

        if (!isKept || !hasLight || length > maxLength)
        {
            return;
        }

        var color = Vector3.Truncate(renderColor);

        if (color == Vector3.Zero)
        {
            return;
        }

        var endWidth = Math.Clamp(width * length / maxLength, 0f, MaxSpotlightWidth);
        var radius = SpotlightLightWidthScale * endWidth * SpotlightLightRadiusScale;

        if (radius <= 0f)
        {
            return;
        }

        var position = end + new Vector3(0f, 0f, SpotlightLightHeight);

        map.SpotlightLights.Add(new Source1ConvertedMap.SpotlightLight(position, color, radius));
    }
}
