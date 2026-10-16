using System.Globalization;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The particle effect an entity plays, with its control points at the entities the <c>cpointN</c> keys
/// name.
/// </summary>
public class ParticleHelper : EntityHelper
{
    private const int ControlPointCount = 64;

    private static readonly string[] ControlPointKeys = CreateControlPointKeys();

    /// <summary>Gets the key of the entity that names the effect.</summary>
    public string EffectKey { get; }

    /// <summary>Gets the effect, once it is in the scene and it loaded.</summary>
    public ParticleSceneNode? Effect { get; private set; }

    /// <summary>Initializes the effect of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public ParticleHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        EffectKey = GetArgument(0) ?? "effect_name";
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var scene = Owner.Scene;
        var fileLoader = scene.RendererContext.FileLoader;
        var logger = scene.RendererContext.Logger;
        var keyValues = Owner.KeyValues;
        var effectName = keyValues.GetStringProperty(EffectKey);

        if (string.IsNullOrEmpty(effectName))
        {
            return;
        }

        if (fileLoader.LoadFileCompiled(effectName)?.DataBlock is not ParticleSystem particleSystem)
        {
            logger.LogWarning("Effect \"{Effect}\" of a '{Classname}' does not exist", effectName, Owner.Classname);
            return;
        }

        var snapshotFile = keyValues.GetStringProperty("snapshot_file");
        var snapshot = string.IsNullOrEmpty(snapshotFile)
            ? null
            : fileLoader.LoadFileCompiled(snapshotFile)?.GetBlockByType(BlockType.SNAP) as ParticleSnapshot;

        try
        {
            Effect = new ParticleSceneNode(scene, particleSystem, snapshot, playedByEntity: true)
            {
                Name = effectName,
                Transform = Owner.Transform,
                LayerName = Scene.ParticlesLayerName,
            };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Failed to set up effect \"{Effect}\" of a '{Classname}'", effectName, Owner.Classname);
            return;
        }

        Owner.AddNode(Effect);
    }

    /// <inheritdoc/>
    public override void Link()
    {
        if (Effect == null)
        {
            return;
        }

        var keyValues = Owner.KeyValues;

        for (var index = 0; index < ControlPointCount; index++)
        {
            var targetName = keyValues.GetStringProperty(ControlPointKeys[index]);

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
    {
        ArgumentNullException.ThrowIfNull(effect);

        var keyValues = Owner.KeyValues;

        if (GetControlPointIndex("data_cp") is { } dataControlPoint)
        {
            effect.GetControlPoint(dataControlPoint).Position = keyValues.GetVector3Property("data_cp_value");
        }

        if (GetControlPointIndex("tint_cp") is { } tintControlPoint)
        {
            effect.GetControlPoint(tintControlPoint).Position = keyValues.GetVector3Property("tint_cp_color", new Vector3(255f));
        }
    }

    private int? GetControlPointIndex(string key)
        => Owner.KeyValues.GetInt32Property(key, -1) is var index && index >= 0 && index < ControlPointCount ? index : null;

    private static string[] CreateControlPointKeys()
    {
        var keys = new string[ControlPointCount];

        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = string.Create(CultureInfo.InvariantCulture, $"cpoint{i}");
        }

        return keys;
    }
}
