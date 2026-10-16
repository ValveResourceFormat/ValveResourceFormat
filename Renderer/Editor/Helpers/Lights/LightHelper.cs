using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The light an entity casts, as its keys describe it, such as the light of the sun. It draws nothing while
/// its entity is selected.
/// </summary>
public sealed class LightHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => true;

    /// <summary>Gets the light, once it is in the scene.</summary>
    public SceneLight? Light { get; private set; }

    /// <summary>Initializes the light of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public LightHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        Light = AddLight(Owner);
    }

    /// <summary>Adds the light an entity casts to its scene.</summary>
    /// <param name="owner">The entity.</param>
    /// <returns>The light, or <see langword="null"/> when the class of the entity casts none.</returns>
    internal static SceneLight? AddLight(IHelperOwner owner)
    {
        var (accepted, type) = SceneLight.IsAccepted(owner.Classname);

        if (!accepted)
        {
            return null;
        }

        var scene = owner.Scene;
        var light = SceneLight.FromEntityProperties(scene, type, owner.KeyValues);

        light.Flags |= ObjectTypeFlags.NoShadows;

        // A light that is off stays in the scene without brightness, so the lights stored with the map
        // keep the slots they were baked into
        if (!light.Enabled)
        {
            light.BrightnessScale = 0f;
            light.Enabled = true;
        }

        light.PlaceAt(owner.Transform);
        owner.AddNode(light, followsEntity: true);

        if (light.UsesOmni2Faces && owner.KeyValues.GetBooleanProperty("showlight"))
        {
            owner.AddNode(new LuminaireSceneNode(scene, light));
        }

        return light;
    }
}
