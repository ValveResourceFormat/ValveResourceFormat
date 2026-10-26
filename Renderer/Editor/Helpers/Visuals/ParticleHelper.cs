using ValveResourceFormat.IO.Fgd;
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
    /// <summary>Gets the key of the entity that names the effect.</summary>
    private string EffectKey { get; }

    /// <summary>Gets the effect, once it is in the scene and it loaded.</summary>
    private ParticleSceneNode? Effect { get; set; }

    private protected ParticleHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        EffectKey = GetEffectKey(declaration);
    }

    /// <summary>Creates the effect a class declares.</summary>
    /// <returns>The effect, or <see langword="null"/> when the entity names none that exists, so that the entity is drawn some other way.</returns>
    internal static ParticleHelper? Create(IHelperOwner owner, HelperDeclaration declaration)
        => HasEffect(owner, declaration) ? new ParticleHelper(owner, declaration) : null;

    /// <summary>Gets whether the entity names an effect that exists.</summary>
    private protected static bool HasEffect(IHelperOwner owner, HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return owner.KeyValues.GetStringProperty(GetEffectKey(declaration)) is { Length: > 0 } effectName && CompiledFileExists(owner, effectName);
    }

    private static string GetEffectKey(HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count > 0 ? declaration.Arguments[0] : "effect_name";
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
