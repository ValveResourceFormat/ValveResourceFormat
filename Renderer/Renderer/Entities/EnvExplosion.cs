using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_explosion</c>. Plays <c>explosion_custom_effect</c> at the entity each time it is told to
/// <c>Explode</c>. The damage and the default fireball are not simulated.
/// </summary>
public sealed class EnvExplosion : BaseEntity
{
    /// <summary>Gets the explosion effect, or <see langword="null"/> when the entity names none.</summary>
    public ParticleSceneNode? Effect { get; private set; }

    /// <summary>Initializes an <c>env_explosion</c> from its keyvalues.</summary>
    public EnvExplosion(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Effect = CreateEffect(KeyValues.GetStringProperty("explosion_custom_effect"));

        if (Effect == null)
        {
            return;
        }

        // Nothing until the first Explode
        Effect.Stop();

        AddNode(Effect);
    }

    [EntityInput("Explode")]
    private void InputExplode(EntityInputData data) => Effect?.Play();
}
