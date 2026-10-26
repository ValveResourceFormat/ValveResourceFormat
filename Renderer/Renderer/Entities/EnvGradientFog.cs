using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>env_gradient_fog</c>. Sets the distance and height fog of its scene.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CGradientFog">CGradientFog</seealso>
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
        base.Spawn();

        var entity = KeyValues;
        var fogInfo = Scene.FogInfo;

        // Off until an Enable input, which is not simulated. A 3D sky can carry one, and taking it would
        // replace the map's own fog for the whole sky view.
        if (entity.GetBooleanProperty("startdisabled"))
        {
            return;
        }

        fogInfo.GradientFogActive = true;

        Fog = SceneGradientFog.FromEntityProperties(Scene, entity);
        fogInfo.GradientFog = Fog;
    }
}
