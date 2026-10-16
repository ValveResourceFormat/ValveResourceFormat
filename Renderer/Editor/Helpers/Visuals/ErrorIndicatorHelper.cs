using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// An icon over everything else that marks an entity whose class must be placed under an entity of another
/// class, while it is not: it is under no entity, or under one of a class that is not that class or based on it.
/// </summary>
public sealed class ErrorIndicatorHelper : IconHelper
{
    private const string ErrorMaterialName = "materials/editor/error_indicator.vmat";
    private const string RequiredParentClassKey = "required_parent_class";

    /// <summary>Initializes the marker of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public ErrorIndicatorHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration, ErrorMaterialName)
    {
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (IsMisplaced())
        {
            base.AddToScene();
        }
    }

    // Only a map being edited places entities under others, so the entities of a compiled map are never marked
    private bool IsMisplaced()
    {
        if (Owner is not MapEntity mapEntity || Owner.FgdClass?.Metadata is not { } metadata || !metadata.ContainsKey(RequiredParentClassKey)
            || metadata.GetStringProperty(RequiredParentClassKey) is not { Length: > 0 } requiredName)
        {
            return false;
        }

        var rendererContext = Owner.Scene.RendererContext;
        var gameData = EditorFgd.Load(rendererContext.FileLoader, rendererContext.Logger);

        // A class the game does not declare cannot be required
        if (gameData.FindClass(requiredName) is not { } requiredClass)
        {
            return false;
        }

        return mapEntity.PlacedNode.Parent?.CreateEntity()?.GetStringProperty("classname") is not { Length: > 0 } parentClassname
            || gameData.FindClass(parentClassname) is not { } parentClass
            || !DerivesFrom(parentClass, requiredClass);
    }

    private static bool DerivesFrom(FgdClass gameClass, FgdClass baseClass)
        => gameClass == baseClass
            || gameClass.Name.Equals(baseClass.Name, StringComparison.OrdinalIgnoreCase)
            || gameClass.BaseClasses.Exists(parent => DerivesFrom(parent, baseClass));
}
