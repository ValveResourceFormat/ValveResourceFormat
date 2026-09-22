using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>info_map_parameters</c>. Sets the map-wide wetness and puddle ripple parameters of its scene.</summary>
public sealed class InfoMapParameters : BaseEntity
{
    /// <summary>Initializes an <c>info_map_parameters</c> from its keyvalues.</summary>
    public InfoMapParameters(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Scene.EnvironmentWetness = new Vector4(
            KeyValues.GetFloatProperty("envwetnesscoverage", 1f),
            KeyValues.GetFloatProperty("envwetnessdryingamount", 0f),
            KeyValues.GetFloatProperty("envrainstrength", 1f),
            KeyValues.GetFloatProperty("envpuddleripplestrength", 1f));

        // raintracetoskyenabled

        Scene.PuddleWindDirection = KeyValues.GetFloatProperty("envpuddlerippledirection", 0f);
    }
}
