using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The view of a node of a camera path, a frustum along +X of the node from its <c>FOV</c>, <c>ZNear</c>,
/// <c>ZFar</c>, <c>rendercolor</c> and <c>aspectRatio</c> keys, drawn while the node is all that is selected.
/// A node with no field of view of its own has the one of the view, and the nodes of a path with
/// <c>relative_fov</c> set add theirs to it.
/// </summary>
internal sealed class CameraPathNodeHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool RequiresSelection => true;

    private const string NearPlaneKey = "znear";
    private const string FarPlaneKey = "zfar";
    private const string ColorKey = "rendercolor";
    private const string AspectRatioKey = "aspectratio";

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsOnlySelected)
        {
            return;
        }

        CameraFrustum.DrawEntityView(context, Owner.Transform, GetFov(), GetFloatKey(NearPlaneKey, CameraFrustum.DefaultNearPlane),
            GetFloatKey(FarPlaneKey, CameraFrustum.DefaultFarPlane), GetFloatKey(AspectRatioKey, CameraFrustum.DefaultAspectRatio),
            GetColorKey(ColorKey, CameraFrustum.DefaultColor));
    }

    private float GetFov()
    {
        var fov = GetFloatKey(CameraFrustum.FovKey, CameraFrustum.DefaultFov);

        if (Owner.Path is { } path && path.KeyValues.GetInt32Property(CameraFrustum.RelativeFovKey) != 0)
        {
            return CameraFrustum.ViewFov + fov;
        }

        return fov > 0f ? fov : CameraFrustum.ViewFov;
    }
}
