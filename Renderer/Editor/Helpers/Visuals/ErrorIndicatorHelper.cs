using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// An icon over everything else that marks an entity whose class must be placed under an entity of another
/// class, while it is not: it is under no entity, or under one of a class that is not that class or based on it.
/// </summary>
internal sealed class ErrorIndicatorHelper : IconHelper
{
    private const string ErrorMaterialName = "materials/editor/error_indicator.vmat";

    public ErrorIndicatorHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration, ErrorMaterialName)
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
        if (Owner is not MapEntity mapEntity || Owner.EntityClass?.RequiredParentClass is not { Length: > 0 } requiredName)
        {
            return false;
        }

        var rendererContext = Owner.Scene.RendererContext;
        var classes = EntityClassCatalog.Get(rendererContext.FileLoader, rendererContext.Logger);

        // A class the game does not declare cannot be required
        if (!classes.Declares(requiredName))
        {
            return false;
        }

        return mapEntity.PlacedNode.Parent?.CreateEntity()?.GetStringProperty("classname") is not { Length: > 0 } parentClassname
            || classes.FindClass(parentClassname) is not { } parentClass
            || !parentClass.DerivesFrom(requiredName);
    }
}
