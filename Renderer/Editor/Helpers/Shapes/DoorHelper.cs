using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// Where a selected door that spawns ajar stands, its bounds turned open by its <c>ajarangle</c> key: about
/// its <c>hinge_axis</c> key in the space of the door, or about its up axis where that is not set.
/// </summary>
internal sealed class DoorHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    private const int SpawnPositionAjar = 3;

    private const byte FaceAlpha = 64;
    private const byte EdgeAlpha = 128;
    private const float EdgeWidth = 2f;

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        // The bounds of the door are those of its model
        if (!state.IsSelected || GetIntegerKey("spawnpos") != SpawnPositionAjar
            || Owner.LocalBounds is not { } bounds)
        {
            return;
        }

        var color = GetDrawColor(state);

        context.PushTransform(GetAjarRotation() * Owner.RigidTransform);

        context.PushRenderMode(ToolRenderMode.TranslucentFlat);
        context.DrawColor = color with { A = FaceAlpha };
        context.DrawSolidBox(bounds.Min, bounds.Max, doubleSided: true);
        context.PopRenderMode();

        context.PushRenderMode(ToolRenderMode.WireframeDepthBias);
        context.LineWidth = EdgeWidth;
        context.DrawColor = color with { A = EdgeAlpha };
        context.DrawWireBox(bounds.Min, bounds.Max);
        context.LineWidth = 0f;
        context.PopRenderMode();

        context.PopTransform();
    }

    // How far the door is turned open, in its own space
    private Matrix4x4 GetAjarRotation()
    {
        var angle = GetFloatKey("ajarangle");
        var hingeAxis = GetVector3Key("hinge_axis");

        if (hingeAxis == Vector3.Zero)
        {
            return EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, angle, 0f));
        }

        return angle == 0f ? Matrix4x4.Identity : Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(hingeAxis), float.DegreesToRadians(angle));
    }
}
