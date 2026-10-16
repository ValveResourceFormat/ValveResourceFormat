using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// An arrow at an entity along a direction, over its forward, left and up axes: the angles one of its keys
/// holds, or the way the entity itself faces. It is drawn whether or not the entity is selected, and while it
/// is, over everything with a solid head.
/// </summary>
public sealed class AnglesHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    private const float ShaftLength = 32f;
    private const float HeadLength = 16f;
    private const float HeadRadius = 4f;
    private const float AxisLength = 16f;

    /// <summary>Gets the key of the entity that holds the angles, or <see langword="null"/> for its own.</summary>
    public string? AnglesKey { get; }

    /// <summary>Gets whether the angles are relative to the entity.</summary>
    public bool IsLocal { get; }

    /// <summary>Gets whether the forward, left and up axes are drawn under the arrow.</summary>
    public bool DrawBasisVectors { get; }

    /// <summary>Gets the color of the arrow.</summary>
    public Color32 Color { get; }

    /// <summary>Initializes the direction arrow of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public AnglesHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        IsLocal = declaration.Name.Equals("drawangles_local", StringComparison.OrdinalIgnoreCase);
        AnglesKey = GetMetadata("angles_key")?.ToLowerInvariant();
        DrawBasisVectors = GetMetadataFlag("draw_basis_vectors", true);
        Color = GetMetadataColor("forward_color") ?? Color32.White;
    }

    /// <inheritdoc/>
    public override void AddToScene() => AddShapes();

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

    // The angles in the key, turned with the entity when they are local to it, or the entity's own
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
