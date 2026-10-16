using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The sky of a sky entity, drawn behind everything else of the map.
/// </summary>
public sealed class SkyboxHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => true;

    /// <summary>Gets the material of the sky, or <see langword="null"/> when the entity names none.</summary>
    public string? MaterialName { get; }

    /// <summary>Gets what the brightness of the sky is scaled by.</summary>
    public float BrightnessScale { get; }

    /// <summary>Initializes the sky of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public SkyboxHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        MaterialName = GetMaterialName(owner.KeyValues);
        BrightnessScale = GetBrightnessScale(owner.KeyValues);
    }

    /// <summary>Gets the sky material a sky entity names.</summary>
    /// <param name="keyValues">The keyvalues of the entity.</param>
    /// <returns>The name of the material, or <see langword="null"/> when it names none.</returns>
    public static string? GetMaterialName(EntityLump.Entity keyValues)
    {
        ArgumentNullException.ThrowIfNull(keyValues);

        return keyValues.GetStringProperty("skyname") ?? keyValues.GetStringProperty("skybox_material_day");
    }

    /// <summary>Gets what a sky entity scales the brightness of its sky by.</summary>
    /// <param name="keyValues">The keyvalues of the entity.</param>
    /// <returns>The scale. Zero or less leaves it unset, which is one.</returns>
    public static float GetBrightnessScale(EntityLump.Entity keyValues)
    {
        ArgumentNullException.ThrowIfNull(keyValues);

        var brightnessScale = keyValues.GetFloatProperty("brightnessscale", 1f);

        return brightnessScale > 0f ? brightnessScale : 1f;
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var keyValues = Owner.KeyValues;

        if (string.IsNullOrEmpty(MaterialName) || keyValues.GetBooleanProperty("startdisabled") || !keyValues.GetBooleanProperty("enabled", true))
        {
            return;
        }

        var scene = Owner.Scene;
        var rendererContext = scene.RendererContext;

        using var skyMaterial = rendererContext.FileLoader.LoadFileCompiled(MaterialName);

        if (skyMaterial == null)
        {
            return;
        }

        scene.Skybox2D?.Delete();
        scene.Skybox2D = new SceneSkybox2D(rendererContext.MaterialLoader.LoadMaterial(skyMaterial))
        {
            Tint = keyValues.GetColor32Property("tint_color") * BrightnessScale,
            Transform = Owner.Transform with { Translation = Vector3.Zero },
        };
    }
}
