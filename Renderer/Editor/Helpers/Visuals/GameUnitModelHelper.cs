using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The unit or item a key of an entity names. Its model comes from scripts of the game that are not read, so
/// only the small white box that marks the entity is drawn, which does not turn with it.
/// </summary>
public sealed class GameUnitModelHelper : EntityHelper
{
    private static readonly Vector3 BoxMins = new(-8f);
    private static readonly Vector3 BoxMaxs = new(8f);

    private GameUnitModelHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override AABB? ObjectBounds => new(Owner.Transform.Translation + BoxMins, Owner.Transform.Translation + BoxMaxs);

    /// <summary>Creates the marker a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <returns>The marker, or <see langword="null"/> when the declaration does not name exactly one key.</returns>
    public static GameUnitModelHelper? Create(IHelperOwner owner, FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count == 1 ? new GameUnitModelHelper(owner, declaration) : null;
    }

    /// <inheritdoc/>
    public override void AddToScene() => AddShapes(Matrix4x4.CreateTranslation(Owner.Transform.Translation));

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var origin = Owner.Transform.Translation;
        var (mins, maxs) = (origin + BoxMins, origin + BoxMaxs);

        // Lit white faces, or tinted while selected and outlined then too
        var color = state.IsSelected ? SelectionOverlayColor : Color32.White;

        context.PushRenderMode(ToolRenderMode.Flat);
        context.DrawBoxFaces(mins, maxs, color);
        context.PopRenderMode();

        if (!state.IsSelected)
        {
            return;
        }

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = color;
        context.DrawBoxFaceOutlines(mins, maxs);
        context.PopRenderMode();
    }
}
