using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A model that plays an ambient effect for as long as it exists: Dota's buildings, towers, trees and
/// fountains (<c>ambientfx</c>), and Artifact's board attachments (<c>ambienteffect</c>).
/// </summary>
/// <remarks>
/// Only the ambient effect is played. The ones these entities name for being damaged or destroyed need
/// game events the viewer does not have.
/// </remarks>
public sealed class AmbientEffectEntity : BaseModelEntity
{
    /// <summary>Gets the ambient effect, or <see langword="null"/> when the entity names none.</summary>
    public ParticleSceneNode? Effect { get; private set; }

    /// <summary>Initializes the entity from its keyvalues.</summary>
    public AmbientEffectEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    // Walked through, like other scenery
    /// <inheritdoc/>
    protected override bool BuildsCollider => false;

    /// <inheritdoc/>
    public override void Spawn()
    {
        Effect = CreateEffect(KeyValues.GetStringProperty("ambientfx") ?? KeyValues.GetStringProperty("ambienteffect"));

        if (Effect == null)
        {
            return;
        }

        // Control point 0 follows the model's attach_fx attachment, where it has one. 1 stays where the
        // entity spawned and 2 carries its angles.
        if (ModelNode is { } modelNode && modelNode.HasAttachmentOrBone("attach_fx"))
        {
            AddNode(Effect, followsEntity: false);
            modelNode.AttachNode(Effect, "attach_fx");
        }
        else
        {
            AddNode(Effect);
        }

        Effect.GetControlPoint(1).Position = Transform.Translation;
        Effect.GetControlPoint(2).Position = Angles;

        // Only a tint other than white is handed to the effect, which then reads it off control point 15
        var tint = KeyValues.GetColor32Property("particle_tint_color") * 255f;

        if (KeyValues.ContainsKey("particle_tint_color") && tint != new Vector3(255f))
        {
            Effect.GetControlPoint(15).Position = tint;
            Effect.GetControlPoint(16).Position = new Vector3(1f, 0f, 0f);
        }
    }
}
