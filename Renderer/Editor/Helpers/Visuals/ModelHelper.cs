using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The model an entity is drawn as: the one its class names, or the one its <c>model</c> key does. A class
/// declares it as <c>editormodel</c> where the model only stands in for the entity in the editor.
/// </summary>
public sealed class ModelHelper : EntityHelper
{
    private const string ErrorModelName = "models/dev/error.vmdl";

    // What a class can put where the name of the model goes, to draw the model the entity names differently
    private static readonly HashSet<string> Options = new(StringComparer.OrdinalIgnoreCase)
    {
        "static", "fixedbounds", "castshadows", "nobounds", "posable", "postocclusionquery",
    };

    /// <inheritdoc/>
    public override bool IsVisual => true;

    /// <summary>Gets whether the model only stands in for the entity in the editor.</summary>
    public bool IsEditorModel { get; }

    /// <summary>Gets the model, or <see langword="null"/> when neither the class nor the entity names one.</summary>
    public string? ModelName { get; }

    /// <summary>
    /// Gets whether the class names the model, which then stands in for every entity of the class, rather
    /// than each entity naming its own with its <c>model</c> key.
    /// </summary>
    public bool IsNamedByClass { get; }

    /// <summary>Initializes the model of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public ModelHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        IsEditorModel = declaration.Name.Equals("editormodel", StringComparison.OrdinalIgnoreCase);

        var name = GetDeclaredModelName(declaration);

        IsNamedByClass = name != null;
        name ??= owner.KeyValues.GetStringProperty("model");

        ModelName = string.IsNullOrEmpty(name) ? null : GetModelName(name);
    }

    /// <summary>Gets the model a class names in the declaration of a model helper.</summary>
    /// <param name="declaration">The helper as a class declares it.</param>
    /// <returns>The name as it is declared, or <see langword="null"/> when the model is the one each entity names.</returns>
    public static string? GetDeclaredModelName(FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count > 0 && declaration.Arguments[0] is { Length: > 0 } argument && !Options.Contains(argument)
            ? argument
            : null;
    }

    /// <summary>Gets the model a class or an entity names, as a vmdl.</summary>
    /// <param name="name">The name as it is declared.</param>
    /// <returns>The name of the model.</returns>
    public static string GetModelName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        name = name.Replace('\\', '/');

        if (name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^4] + ".vmdl";
        }

        return name.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) ? name : name + ".vmdl";
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (ModelName == null)
        {
            return;
        }

        var scene = Owner.Scene;
        var fileLoader = scene.RendererContext.FileLoader;
        var keyValues = Owner.KeyValues;
        var name = ModelName;

        if (fileLoader.LoadFileCompiled(name)?.DataBlock is not Model model)
        {
            scene.RendererContext.Logger.LogWarning("Model \"{Model}\" of a '{Classname}' does not exist", name, Owner.Classname);

            if (fileLoader.LoadFileCompiled(ErrorModelName)?.DataBlock is not Model errorModel)
            {
                return;
            }

            model = errorModel;
            name = ErrorModelName;
        }

        if (IsEditorModel)
        {
            var preview = HammerEntityVisuals.IsCamera(Owner.Classname)
                ? new CameraSceneNode(scene, model)
                : new ModelSceneNode(scene, model, null, isWorldPreview: true) { Name = name };

            preview.Transform = Owner.Transform;
            preview.LayerName = HammerEntityVisuals.MarkerLayerName;
            preview.KeepsMarkerSize = true;

            if (SceneLight.IsAccepted(Owner.Classname).Accepted)
            {
                // NoShadows so the model of a light cannot shadow the light
                preview.Flags |= ObjectTypeFlags.NoShadows;
                preview.TintAlpha = new Vector4(SceneLight.GetEditorTint(keyValues), 1f);
            }

            preview.SetAnimationForWorldPreview("tools_preview");
            Owner.AddNode(preview, followsEntity: true);
            return;
        }

        var node = new ModelSceneNode(scene, model, keyValues.GetStringProperty("skin"))
        {
            Name = name,
            Transform = Owner.Transform,
            TintAlpha = keyValues.GetRenderTint(),
        };

        if (keyValues.GetBooleanProperty("disableshadows"))
        {
            node.Flags |= ObjectTypeFlags.NoShadows;
        }

        if (keyValues.GetStringProperty("defaultanim") is { Length: > 0 } animation)
        {
            node.SetAnimationForWorldPreview(animation);
        }

        Owner.AddNode(node, followsEntity: true);
    }
}
