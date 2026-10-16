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
/// The icon an entity is placed as: a material drawn at the entity, facing the camera, with a border around
/// it while the entity is selected.
/// </summary>
public class IconHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    private const float MinimumTintValue = 0.3f;

    private const string SelectionBorderMaterialName = "materials/editor/iconhelper_border.vmat";

    // A yellow frame two pixels wide around a selected icon, over the scene
    private static readonly SpriteBorder SelectionBorder = new(new Vector4(1f, 1f, 0f, 1f), 2f);

    // An icon over the scene is not hidden by anything, and has no border
    private readonly bool isDrawnOverScene;

    private SpriteSceneNode? selectionBorder;

    /// <summary>Gets the material of the icon.</summary>
    public string MaterialName { get; }

    /// <summary>Gets the key of the entity that tints the icon, or <see langword="null"/> when none does.</summary>
    public string? TintKey { get; }

    /// <inheritdoc/>
    /// <remarks>Only where the icon is placed counts, not the size it is drawn at.</remarks>
    public override AABB? ObjectBounds => new(Owner.Transform.Translation, Owner.Transform.Translation);

    /// <summary>Initializes the icon of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public IconHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        MaterialName = GetDeclaredMaterialName(declaration) ?? GetMaterialName(string.Empty);
        TintKey = GetMetadata("tintKey");
    }

    /// <summary>Initializes an icon drawn over everything else, which no key tints.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <param name="materialName">The material of the icon.</param>
    private protected IconHelper(IHelperOwner owner, FgdHelper declaration, string materialName) : base(owner, declaration)
    {
        MaterialName = materialName;
        isDrawnOverScene = true;
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
        var rendererContext = Owner.Scene.RendererContext;

        // An icon whose material does not exist is not drawn
        if (rendererContext.FileLoader.LoadFileCompiled(MaterialName) is not { } resource)
        {
            rendererContext.Logger.LogDebug("Icon \"{Material}\" of class '{Classname}' does not exist", MaterialName, Owner.Classname);
            return;
        }

        var tint = GetTint();

        if (!isDrawnOverScene && rendererContext.FileLoader.LoadFileCompiled(SelectionBorderMaterialName) is { } borderResource)
        {
            selectionBorder = CreateSprite(borderResource, SelectionBorderMaterialName, tint, SelectionBorder);
            selectionBorder.Visible = false;
            Owner.AddNode(selectionBorder, followsEntity: true);
        }

        Owner.AddNode(CreateSprite(resource, MaterialName, tint), followsEntity: true);
    }

    private protected override void OnSelectionChanged(bool isSelected)
    {
        if (selectionBorder != null)
        {
            selectionBorder.Visible = isSelected;
        }
    }

    private SpriteSceneNode CreateSprite(Resource resource, string name, Vector3 tint, SpriteBorder? border = null)
    {
        var scene = Owner.Scene;

        return new SpriteSceneNode(scene, scene.RendererContext, resource, Owner.Transform.Translation, border)
        {
            // An icon over the scene shows whatever is shown of the entity
            LayerName = isDrawnOverScene ? null : HammerEntityVisuals.MarkerLayerName,
            Name = name,
            Flags = ObjectTypeFlags.NoShadows,
            KeepsMarkerSize = true,
            IsDrawnOverScene = isDrawnOverScene || border != null,
            TintAlpha = new Vector4(tint, 1f),
        };
    }

    // A dark tint is brightened to a value of at least 0.3, keeping its hue and saturation, so the icon still shows
    private Vector3 GetTint()
    {
        if (string.IsNullOrEmpty(TintKey)
            || !Owner.KeyValues.ContainsKey(TintKey) && Owner.FgdClass?.FindVariable(TintKey) is not { HasDefault: true })
        {
            return Vector3.One;
        }

        var tint = GetVector3Key(TintKey) / byte.MaxValue;
        var value = tint.MaxComponent();

        return value > 0f ? tint * (MathF.Max(MinimumTintValue, value) / value) : new Vector3(MinimumTintValue);
    }
}
