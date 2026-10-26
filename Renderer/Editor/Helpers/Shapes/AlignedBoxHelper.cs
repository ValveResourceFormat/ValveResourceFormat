using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The box an entity is drawn as: the size its class declares, or a small one where nothing else of the
/// entity is seen. It sits at the entity as lit solid faces in the draw color of the entity, and does not turn
/// with it.
/// </summary>
internal sealed class AlignedBoxHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsStandIn => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    /// <summary>Gets the smallest corner of the box, from the origin of the entity.</summary>
    private Vector3 Mins { get; }

    /// <summary>Gets the largest corner of the box, from the origin of the entity.</summary>
    private Vector3 Maxs { get; }

    public AlignedBoxHelper(IHelperOwner owner, Vector3 mins, Vector3 maxs)
        : base(owner, new HelperDeclaration { Name = "wirebox" })
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

        DrawLitBox(context, origin + Mins, origin + Maxs, UnselectedEntityColor, state);
    }
}
