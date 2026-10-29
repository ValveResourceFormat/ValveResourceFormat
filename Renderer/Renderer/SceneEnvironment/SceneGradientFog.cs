using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Scene node representing distance and height-based gradient fog.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CGradientFog">CGradientFog</seealso>
public class SceneGradientFog(Scene scene) : SceneNode(scene)
{
    /// <summary>Gets or sets the distance at which fog begins.</summary>
    public float StartDist { get; set; }

    /// <summary>Gets or sets the distance at which fog reaches full density.</summary>
    public float EndDist { get; set; }

    /// <summary>Gets or sets the exponent controlling the horizontal fog density falloff.</summary>
    public float FalloffExponent { get; set; }

    /// <summary>Gets or sets the world-space height at which height fog is at full density.</summary>
    public float HeightStart { get; set; }

    /// <summary>Gets or sets the world-space height at which height fog fades to zero.</summary>
    public float HeightEnd { get; set; }

    /// <summary>Gets or sets the exponent controlling the vertical fog density falloff.</summary>
    public float VerticalExponent { get; set; }

    /// <summary>Gets or sets the base fog color.</summary>
    public Vector3 Color { get; set; }

    /// <summary>Gets or sets the strength multiplier applied to the fog color.</summary>
    public float Strength { get; set; }

    /// <summary>Gets or sets the maximum opacity the fog can reach.</summary>
    public float MaxOpacity { get; set; }

    /// <summary>
    /// Returns a <see cref="Vector4"/> encoding the distance bias, height bias, distance scale, and height scale for shader use.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector4 GetBiasAndScale(FogSpace fogSpace)
    {
        var startDist = fogSpace.Distance(StartDist);
        var endDist = fogSpace.Distance(EndDist);

        var distScale = 1f / (endDist - startDist);
        var distBias = -(startDist * distScale);

        var heightStart = fogSpace.Height(HeightStart);
        var heightEnd = fogSpace.Height(HeightEnd);

        // this might be same as cubemap fog height calculations
        // Doesn't seem to be the case for SteamVR, it cuts off too early at the top
        var heightScale = 0f;
        var heightBias = 1f;

        // Without a height range every height is fully in the fog, rather than the scale dividing by zero
        if (heightStart != heightEnd)
        {
            heightScale = 1f / (heightStart - heightEnd);
            heightBias = -(heightEnd * heightScale);
        }

        return new Vector4(distBias, heightBias, distScale, heightScale);
    }

    /// <summary>Gets the horizontal and vertical falloff exponents packed as a <see cref="Vector2"/>.</summary>
    public Vector2 Exponents => new(FalloffExponent, VerticalExponent);

    /// <summary>Gets the color scaled by strength and the max opacity packed as a <see cref="Vector4"/>.</summary>
    public Vector4 Color_Opacity => new(Color * Strength, MaxOpacity);

    /// <summary>
    /// Gets the squared start distance and the culling height packed as a <see cref="Vector2"/> for GPU culling.
    /// The culling height is where the height term reaches zero, or the height start in Half-Life: Alyx.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector2 CullingParams(FogSpace fogSpace)
    {
        var startDist = fogSpace.Distance(StartDist);
        var cullingHeight = Scene.RendererContext.FileLoader.GameInfo.Game == Game.Hlvr ? HeightStart : HeightEnd;

        return new Vector2(startDist * startDist, fogSpace.Height(cullingHeight));
    }

    /// <summary>Creates the fog an <c>env_gradient_fog</c> entity describes.</summary>
    /// <param name="scene">The scene the fog is for.</param>
    /// <param name="entity">The keyvalues of the entity.</param>
    /// <returns>The fog.</returns>
    public static SceneGradientFog FromEntityProperties(Scene scene, EntityLump.Entity entity)
    {
        var distExponent = entity.GetFloatProperty("fogfalloffexponent");
        var startDist = entity.GetFloatProperty("fogstart");
        var endDist = entity.GetFloatProperty("fogend");

        // Some maps don't have these properties.
        var useHeightFog = entity.ContainsKey("fogverticalexponent"); // The oldest versions lack these values, so disable it there
        var useHeightFog2 = entity.ContainsKey("fogstartheight"); // Robot Repair lacks these values, so disable it there
        useHeightFog = entity.GetBooleanProperty("heightfog", useHeightFog); // New in CS2

        // TODO: find the correct behavior under this condition
        var startHeight = entity.GetFloatProperty("fogstartheight");
        var endHeight = entity.GetFloatProperty("fogendheight");
        var heightExponent = entity.GetFloatProperty("fogverticalexponent");

        var strength = entity.GetFloatProperty("fogstrength");
        var color = entity.GetColor32Property("fogcolor");
        var maxOpacity = entity.GetFloatProperty("fogmaxopacity");

        if (!useHeightFog && entity.ContainsKey("heightfog"))
        {
            // Height fog turned off leaves the height term at 1 and the rest of the fog as authored
            startHeight = 0f;
            endHeight = 0f;
            heightExponent = 0f;
        }
        else if (!useHeightFog && !useHeightFog2)
        {
            heightExponent = 1.0f; // Need the value for Robot Repair
            startHeight = startDist; // Assuming it's similar to the horizontal distances
            endHeight = endDist; // Assuming it's similar to the horizontal distances
            strength = 1.0f; // Need the value for Robot Repair
            //color = entity.GetColor32Property("fogcolor"); // Need to get from `gradientfogtexture` key value and combine with `color` keyvalue
            maxOpacity = 0.5f; // Need the value for Robot Repair
            distExponent = 2.0f;
        }
        else if (!useHeightFog && useHeightFog2)
        {
            heightExponent = 1.0f; // Need the value for SteamVR
            strength = 1.0f; // Need the value for SteamVR
            //color = entity.GetColor32Property("fogcolor"); // Need to get from `gradientfogtexture` key value
            maxOpacity = 0.5f; // Need the value for SteamVR
            distExponent = 2.0f; // Need the value for SteamVR
        }

        return new SceneGradientFog(scene)
        {
            StartDist = startDist,
            EndDist = endDist,
            FalloffExponent = distExponent,
            HeightStart = startHeight,
            HeightEnd = endHeight,
            VerticalExponent = heightExponent,
            Color = color,
            Strength = strength,
            MaxOpacity = maxOpacity,
        };
    }
}
