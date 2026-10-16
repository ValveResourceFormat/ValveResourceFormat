using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// An arrow at an entity along a direction, over its forward, left and up axes: the angles one of its keys
/// holds, or the way the entity itself faces. It is drawn whether or not the entity is selected, with a solid
/// head while it is.
/// </summary>
public sealed class AnglesHelper : EntityHelper
{
    private const float ShaftLength = 32f;
    private const float HeadLength = 16f;
    private const float HeadRadius = 4f;
    private const float AxisLength = 16f;

    /// <inheritdoc/>
    public override bool IsVisual => true;

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
    public override void AddToScene()
    {
        var basis = GetBasis();

        AddLines(Matrix4x4.CreateTranslation(Owner.RigidTransform.Translation), lines => AddAxes(lines, basis));
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        var basis = GetBasis();
        var lines = shapes.Aligned(HelperPasses.Overlay);

        lines.AddSolidCone(basis.Forward * ShaftLength, basis.Forward * (ShaftLength + HeadLength), HeadRadius, 16, Color);
        AddAxes(lines, basis);
    }

    // The shaft of the arrow, and the axes under it
    private void AddAxes(EntityHelperLines lines, (Vector3 Forward, Vector3 Left, Vector3 Up) basis)
    {
        lines.AddLine(Vector3.Zero, basis.Forward * ShaftLength, Color);

        if (!DrawBasisVectors)
        {
            return;
        }

        lines.AddLine(Vector3.Zero, basis.Forward * AxisLength, Color32.Red);
        lines.AddLine(Vector3.Zero, basis.Left * AxisLength, Color32.Green);
        lines.AddLine(Vector3.Zero, basis.Up * AxisLength, Color32.Blue);
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
