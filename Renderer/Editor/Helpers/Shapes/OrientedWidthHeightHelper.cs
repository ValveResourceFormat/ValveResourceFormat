using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A flat box that turns with an entity, from its origin along its forward and left axes, sized by two of its
/// keys. It is drawn as lit solid faces in the draw color of the entity, or tinted and outlined while the
/// entity is selected.
/// </summary>
internal sealed class OrientedWidthHeightHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    // The size of the box until the keys give it
    private const float DefaultSize = 8f;

    // The box is this thin, below the plane of the entity
    private const float Depth = 0.1f;

    /// <summary>Gets the key of the entity that holds the width.</summary>
    private string WidthKey { get; }

    /// <summary>Gets the key of the entity that holds the height.</summary>
    private string HeightKey { get; }

    private OrientedWidthHeightHelper(IHelperOwner owner, FgdHelper declaration, string widthKey, string heightKey)
        : base(owner, declaration)
    {
        WidthKey = widthKey;
        HeightKey = heightKey;
    }

    /// <summary>Creates the box a class declares.</summary>
    /// <returns>The box, or <see langword="null"/> when the declaration does not give both keys.</returns>
    public static OrientedWidthHeightHelper? Create(IHelperOwner owner, FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        if (declaration.Arguments.Count < 2)
        {
            return null;
        }

        return new OrientedWidthHeightHelper(owner, declaration, ToTruncatedKey(declaration.Arguments[0]), ToTruncatedKey(declaration.Arguments[1]));
    }

    /// <inheritdoc/>
    public override AABB? ObjectBounds => GetBox().Transform(Owner.RigidTransform);

    /// <inheritdoc/>
    public override void AddToScene() => AddShapes();

    // From the entity along its forward and left axes, just behind it
    private AABB GetBox() => new(new Vector3(0f, 0f, -Depth), new Vector3(GetFloatKey(WidthKey, DefaultSize), GetFloatKey(HeightKey, DefaultSize), 0f));

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var box = GetBox();

        context.PushTransform(Owner.RigidTransform);
        DrawLitBox(context, box.Min, box.Max, UnselectedEntityColor, state);
        context.PopTransform();
    }
}
