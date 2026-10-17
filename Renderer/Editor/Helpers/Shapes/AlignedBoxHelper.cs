using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The box an entity is drawn as: the size its class declares, or a small one where nothing else of the
/// entity is seen. It sits at the entity as lit solid faces in the draw color of the entity, and does not turn
/// with it.
/// </summary>
public sealed class AlignedBoxHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsStandIn => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    /// <summary>Gets the smallest corner of the box, from the origin of the entity.</summary>
    public Vector3 Mins { get; }

    /// <summary>Gets the largest corner of the box, from the origin of the entity.</summary>
    public Vector3 Maxs { get; }

    /// <summary>Initializes the box of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="mins">Smallest corner of the box, from the origin of the entity.</param>
    /// <param name="maxs">Largest corner of the box, from the origin of the entity.</param>
    public AlignedBoxHelper(IHelperOwner owner, Vector3 mins, Vector3 maxs)
        : base(owner, new FgdHelper { Name = "wirebox" })
    {
        Mins = mins;
        Maxs = maxs;
    }

    /// <inheritdoc/>
    public override AABB? ObjectBounds => new(Owner.Transform.Translation + Mins, Owner.Transform.Translation + Maxs);

    /// <inheritdoc/>
    public override void AddToScene() => AddShapes(Matrix4x4.CreateTranslation(Owner.Transform.Translation));

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var origin = Owner.Transform.Translation;
        var (mins, maxs) = (origin + Mins, origin + Maxs);

        // Lit faces in the draw color, or tinted while selected and outlined then too
        var color = state.IsSelected ? SelectionOverlayColor : GetDrawColor(state);

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
