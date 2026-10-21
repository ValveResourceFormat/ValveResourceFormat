using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The particle effect an entity plays, with its control points at the entities the <c>cpointN</c> keys
/// name.
/// </summary>
internal class ParticleHelper : EntityHelper
{
    /// <inheritdoc/>
    public override GizmoTransformModes SupportedTransformModes => GizmoTransformModes.Translate | GizmoTransformModes.Rotate;

    /// <summary>Gets the key of the entity that names the effect.</summary>
    private string EffectKey { get; }

    /// <summary>Gets the effect, once it is in the scene and it loaded.</summary>
    private ParticleSceneNode? Effect { get; set; }

    public ParticleHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        EffectKey = GetArgument(0) ?? "effect_name";
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var scene = Owner.Scene;
        var keyValues = Owner.KeyValues;
        var snapshot = InfoParticleSystem.LoadSnapshot(scene.RendererContext.FileLoader, keyValues);

        Effect = BaseEntity.CreateEffect(scene, keyValues.GetStringProperty(EffectKey), Owner.Transform, snapshot,
            playedByEntity: true, Owner.Classname, keyValues.GetStringProperty("targetname"));

        if (Effect != null)
        {
            Owner.AddNode(Effect);
        }
    }

    /// <inheritdoc/>
    public override void Link()
    {
        if (Effect == null)
        {
            return;
        }

        var controlPointKeys = InfoParticleSystem.ControlPointKeys;

        for (var index = 0; index < controlPointKeys.Length; index++)
        {
            var targetName = Owner.KeyValues.GetStringProperty(controlPointKeys[index]);

            if (string.IsNullOrEmpty(targetName) || Owner.FindByName(targetName) is not [var target, ..])
            {
                continue;
            }

            // Control point 0 is where the effect is, so naming an entity there moves the whole effect to it
            if (index == 0)
            {
                Effect.Transform = target.Transform;
            }

            Effect.SetControlPoint(index, target.Transform);
        }

        SetControlPoints(Effect);
    }

    /// <summary>Sets the control points the keys of the entity give a value of their own.</summary>
    /// <param name="effect">The effect.</param>
    protected virtual void SetControlPoints(ParticleSceneNode effect)
        => InfoParticleSystem.SetDataAndTintControlPoints(effect, Owner.KeyValues);
}
