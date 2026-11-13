using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// An arrow at an entity along a direction, with the forward, left and up axes of that direction. The direction
/// is the angles held by a key of the entity, or the way the entity itself faces. The arrow is drawn whether or
/// not the entity is selected. While selected, it is drawn over everything with a solid head.
/// </summary>
internal sealed class AnglesHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    private const float ShaftLength = 32f;
    private const float HeadLength = 16f;
    private const float HeadRadius = 4f;
    private const float AxisLength = 16f;

    /// <summary>Gets the key of the entity that holds the angles, or <see langword="null"/> for the angles of the entity itself.</summary>
    private string? AnglesKey { get; }

    /// <summary>Gets whether the angles are relative to the entity.</summary>
    private bool IsLocal { get; }

    /// <summary>Gets whether the forward, left and up axes are drawn under the arrow.</summary>
    private bool DrawBasisVectors { get; }

    /// <summary>Gets the color of the arrow.</summary>
    private Color32 Color { get; }

    public AnglesHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        IsLocal = declaration.Name.Equals("drawangles_local", StringComparison.OrdinalIgnoreCase);
        AnglesKey = GetMetadata("angles_key")?.ToLowerInvariant();
        DrawBasisVectors = GetMetadataFlag("draw_basis_vectors", true);
        Color = GetMetadataColor("forward_color") ?? Color32.White;
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        // Angles of a key that are not local to the entity are an orientation in the world
        var isWorldAligned = AnglesKey != null && !IsLocal;

        AddShapes(isWorldAligned ? ShapePlacement.Origin : ShapePlacement.Entity);
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var (forward, left, up) = GetBasis();
        var origin = Owner.RigidTransform.Translation;

        // The head of the arrow only shows while the entity is selected, over everything as the shaft then is
        if (state.IsSelected)
        {
            context.PushRenderMode(ToolRenderMode.FlatNoZ);
            context.DrawSolidCone(origin + forward * ShaftLength, origin + forward * (ShaftLength + HeadLength), HeadRadius, 16, Color, Color);
            context.PopRenderMode();
        }

        context.PushRenderMode(state.IsSelected ? ToolRenderMode.WireframeNoZ : ToolRenderMode.Wireframe);

        context.LineWidth = 2f;
        context.DrawLine(origin, origin + forward * ShaftLength, Color);
        context.LineWidth = 0f;

        if (DrawBasisVectors)
        {
            context.DrawLine(origin, origin + forward * AxisLength, Color32.Red);
            context.DrawLine(origin, origin + left * AxisLength, Color32.Green);
            context.DrawLine(origin, origin + up * AxisLength, Color32.Blue);
        }

        context.PopRenderMode();
    }

    // Basis of the angles in the key, turned with the entity when they are local, or of the entity itself without a key
    private (Vector3 Forward, Vector3 Left, Vector3 Up) GetBasis()
    {
        var rotation = AnglesKey == null
            ? Owner.RigidTransform
            : EntityTransformHelper.ToRigidTransformationMatrix(GetVector3Key(AnglesKey), Vector3.Zero);

        if (AnglesKey != null && IsLocal)
        {
            rotation *= Owner.RigidTransform;
        }

        return (Vector3.Normalize(rotation.GetRow(0).AsVector3()), Vector3.Normalize(rotation.GetRow(1).AsVector3()), Vector3.Normalize(rotation.GetRow(2).AsVector3()));
    }
}
