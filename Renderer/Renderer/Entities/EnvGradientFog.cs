using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>env_gradient_fog</c>. Sets the distance and height fog of its scene.</summary>
public sealed class EnvGradientFog : BaseEntity
{
    /// <summary>Gets the fog this entity set, or <see langword="null"/> when it did not take effect.</summary>
    public SceneGradientFog? Fog { get; private set; }

    /// <summary>Initializes an <c>env_gradient_fog</c> from its keyvalues.</summary>
    public EnvGradientFog(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var entity = KeyValues;
        var fogInfo = Scene.FogInfo;

        // Off until an Enable input, which is not simulated. A 3D sky can carry one, and taking it would
        // replace the map's own fog for the whole sky view.
        if (entity.GetBooleanProperty("startdisabled"))
        {
            return;
        }

        fogInfo.GradientFogActive = true;

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

        if (!useHeightFog && !useHeightFog2)
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

        Fog = new SceneGradientFog(Scene)
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

        fogInfo.GradientFog = Fog;
    }
}
