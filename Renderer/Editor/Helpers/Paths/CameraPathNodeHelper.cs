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
internal sealed class CameraPathNodeHelper(IHelperOwner owner, FgdHelper declaration) : EntityHelper(owner, declaration)
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

    private const float DefaultFov = 90f;
    private const float DefaultNearPlane = 10f;
    private const float DefaultFarPlane = 10000f;
    private const float DefaultAspectRatio = 1f;

    private const float FaceAlphaScale = 0.5f;

    private static readonly Color32 DefaultColor = new(128, 128, 128, 128);

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsOnlySelected)
        {
            return;
        }

        var color = CameraFrustum.ReadColor(Owner.KeyValues, ColorKey)
            ?? (GetStringKey(ColorKey) is { Length: > 0 } colorText ? CameraFrustum.ParseColor(colorText) : DefaultColor);

        context.PushTransform(Owner.Transform);
        context.LayerOverride = ToolRenderLayer.PostTranslucent;

        CameraFrustum.Draw(context, GetFov(), GetFloatKey(NearPlaneKey, DefaultNearPlane), GetFloatKey(FarPlaneKey, DefaultFarPlane),
            GetFloatKey(AspectRatioKey, DefaultAspectRatio), color with { A = (byte)(int)(color.A * FaceAlphaScale + 0.5f) });

        context.LayerOverride = null;
        context.PopTransform();
    }

    private float GetFov()
    {
        var fov = GetFloatKey(CameraFrustum.FovKey, DefaultFov);

        if (Owner.Path is { } path && path.KeyValues.GetInt32Property(CameraFrustum.RelativeFovKey) != 0)
        {
            return CameraFrustum.ViewFov + fov;
        }

        return fov > 0f ? fov : CameraFrustum.ViewFov;
    }
}
