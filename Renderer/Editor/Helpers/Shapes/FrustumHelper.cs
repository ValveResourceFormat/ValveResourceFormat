using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The view of a camera or projector, a frustum along +X of the entity whose field of view, near and far
/// planes, color and aspect ratio are keys of it. Its faces are tinted and outlined in white while the entity
/// is selected.
/// </summary>
internal sealed class FrustumHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool RequiresSelection => true;

    /// <summary>Gets the key of the entity that holds the field of view, in degrees.</summary>
    private string FovKey { get; } = "_fov";

    /// <summary>Gets the key of the entity that holds the distance to the near plane.</summary>
    private string NearPlaneKey { get; } = "_nearplane";

    /// <summary>Gets the key of the entity that holds the distance to the far plane.</summary>
    private string FarPlaneKey { get; } = "_farplane";

    /// <summary>Gets the key of the entity that holds the color of the faces.</summary>
    private string ColorKey { get; } = "_color";

    /// <summary>Gets the key of the entity that holds the width of the view over its height.</summary>
    private string AspectRatioKey { get; } = "aspectratio";

    /// <summary>Gets whether the field of view is the vertical one, rather than the horizontal one.</summary>
    private bool IsVerticalFov { get; }

    public FrustumHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        // The fifth argument is not read
        if (GetArgument(0) is { } fovKey)
        {
            FovKey = fovKey.ToLowerInvariant();
            IsVerticalFov = fovKey.Equals("VerticalFOV", StringComparison.OrdinalIgnoreCase);
        }

        NearPlaneKey = GetArgument(1)?.ToLowerInvariant() ?? NearPlaneKey;
        FarPlaneKey = GetArgument(2)?.ToLowerInvariant() ?? FarPlaneKey;
        ColorKey = GetArgument(3)?.ToLowerInvariant() ?? ColorKey;
        AspectRatioKey = GetArgument(5)?.ToLowerInvariant() ?? AspectRatioKey;
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var aspectRatio = GetFloatKey(AspectRatioKey, CameraFrustum.DefaultAspectRatio);
        var fov = GetFloatKey(FovKey, CameraFrustum.DefaultFov);

        if (IsVerticalFov)
        {
            fov = float.RadiansToDegrees(MathF.Atan(MathF.Tan(float.DegreesToRadians(fov * 0.5f)) * aspectRatio)) * 2f;
        }

        // Unlike the color of a camera path node, an empty one is the default rather than transparent
        var color = GetStringKey(ColorKey) is { Length: > 0 } colorText ? ParseColor(colorText) : CameraFrustum.DefaultColor;

        CameraFrustum.DrawEntityView(context, Owner.RigidTransform, fov, GetFloatKey(NearPlaneKey, CameraFrustum.DefaultNearPlane),
            GetFloatKey(FarPlaneKey, CameraFrustum.DefaultFarPlane), aspectRatio, color);
    }
}
