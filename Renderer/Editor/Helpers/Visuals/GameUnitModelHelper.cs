using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The unit or item a key of an entity names. Its model comes from scripts of the game that are not read, so
/// only the small white box that marks the entity is drawn, which does not turn with it.
/// </summary>
internal sealed class GameUnitModelHelper : EntityHelper
{
    /// <inheritdoc/>
    public override GizmoTransformModes SupportedTransformModes => GizmoTransformModes.Translate | GizmoTransformModes.Rotate;

    private static readonly Vector3 BoxMins = new(-8f);
    private static readonly Vector3 BoxMaxs = new(8f);

    private GameUnitModelHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override AABB? ObjectBounds => new(Owner.Transform.Translation + BoxMins, Owner.Transform.Translation + BoxMaxs);

    /// <summary>Creates the marker a class declares.</summary>
    /// <returns>The marker, or <see langword="null"/> when the declaration does not name exactly one key.</returns>
    public static GameUnitModelHelper? Create(IHelperOwner owner, HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count == 1 ? new GameUnitModelHelper(owner, declaration) : null;
    }

    /// <inheritdoc/>
    public override void AddToScene() => AddShapes(Matrix4x4.CreateTranslation(Owner.Transform.Translation));

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var origin = Owner.Transform.Translation;

        DrawLitBox(context, origin + BoxMins, origin + BoxMaxs, Color32.White, state);
    }
}
