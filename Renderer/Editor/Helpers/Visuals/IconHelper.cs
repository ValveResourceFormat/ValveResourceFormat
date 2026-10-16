using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The icon an entity is placed as: a material drawn at the entity, facing the camera.
/// </summary>
public sealed class IconHelper : EntityHelper
{
    private const float MinimumTintValue = 0.3f;

    private static readonly Vector3 MissingIconMins = new(-8f);
    private static readonly Vector3 MissingIconMaxs = new(8f);

    /// <inheritdoc/>
    public override bool IsVisual => true;

    /// <summary>Gets the material of the icon.</summary>
    public string MaterialName { get; }

    /// <summary>Gets the key of the entity that tints the icon, or <see langword="null"/> when none does.</summary>
    public string? TintKey { get; }

    /// <summary>Initializes the icon of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public IconHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        MaterialName = GetDeclaredMaterialName(declaration) ?? GetMaterialName(string.Empty);
        TintKey = GetMetadata("tintKey");
    }

    /// <summary>Gets the material a class names in the declaration of an icon.</summary>
    /// <param name="declaration">The helper as a class declares it.</param>
    /// <returns>The name of the material, or <see langword="null"/> when the declaration names none.</returns>
    public static string? GetDeclaredMaterialName(FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var image = declaration.Metadata is { } keys && keys.ContainsKey("image")
            ? keys.GetStringProperty("image")
            : declaration.Arguments.FirstOrDefault();

        return string.IsNullOrEmpty(image) ? null : GetMaterialName(image);
    }

    /// <summary>Gets the material an icon is declared with: in lower case, under materials, as a vmat.</summary>
    /// <param name="image">The icon as a class declares it.</param>
    /// <returns>The name of the material.</returns>
    public static string GetMaterialName(string image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var name = image.ToLowerInvariant();

        if (name.StartsWith("editor/", StringComparison.Ordinal))
        {
            name = "materials/" + name;
        }

        return Path.ChangeExtension(name, ".vmat").Replace('\\', '/');
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var scene = Owner.Scene;
        var resource = scene.RendererContext.FileLoader.LoadFileCompiled(MaterialName);

        if (resource == null)
        {
            scene.RendererContext.Logger.LogDebug("Icon \"{Material}\" of class '{Classname}' does not exist", MaterialName, Owner.Classname);

            // A box stands in for the material every missing material is drawn as, so the entity can still be seen
            new AlignedBoxHelper(Owner, MissingIconMins, MissingIconMaxs).AddToScene();
            return;
        }

        var sprite = new SpriteSceneNode(scene, scene.RendererContext, resource, Owner.Transform.Translation)
        {
            LayerName = HammerEntityVisuals.MarkerLayerName,
            Name = MaterialName,
            Flags = ObjectTypeFlags.NoShadows,
            KeepsMarkerSize = true,
        };

        if (!string.IsNullOrEmpty(TintKey) && Owner.KeyValues.ContainsKey(TintKey))
        {
            // A dark tint is brightened to a value of at least 0.3, keeping its hue and saturation, so the
            // icon still shows
            var tint = Owner.KeyValues.GetColor32Property(TintKey);
            var value = tint.MaxComponent();

            tint = value > 0f ? tint * (MathF.Max(MinimumTintValue, value) / value) : new Vector3(MinimumTintValue);
            sprite.TintAlpha = new Vector4(tint, 1f);
        }

        Owner.AddNode(sprite, followsEntity: true);
    }
}
