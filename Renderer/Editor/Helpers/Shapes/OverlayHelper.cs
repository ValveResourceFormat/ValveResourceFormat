using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The material an overlay projects onto what is behind it, through a box as wide and high as its keys say,
/// centered on the entity by its center keys, and as deep as its depth key on both sides of it. The box is
/// drawn while the entity is selected.
/// </summary>
internal sealed class OverlayHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    // The size along each axis whose key the entity does not have
    private const float DefaultSize = 64f;

    private static readonly Color32 BoundsColor = new(0, 255, 0);

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var size = new Vector3(GetSize("width"), GetSize("height"), GetSize("depth"));

        if (GetStringKey("material") is not { Length: > 0 } material || size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
        {
            return;
        }

        var decals = Owner.Scene.ProjectedDecals;
        var sequence = GetStringKey("sequence");
        var decal = decals.RegisterMaterial(material, sequence.Length > 0 ? sequence : null);

        // The center keys say where on the material the entity is, from its corner
        var center = new Vector3((0.5f - GetFloatKey("centeru")) * size.X, (0.5f - GetFloatKey("centerv")) * size.Y, 0f);
        var box = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center) * Owner.RigidTransform;

        var color = Owner.KeyValues.ContainsKey("rendercolor") ? GetVector3Key("rendercolor") / byte.MaxValue : Vector3.One;

        decals.Add(decal, box, new Vector4(ColorSpace.SrgbGammaToLinear(color), 1f), permanent: true);
    }

    /// <inheritdoc/>
    public override AABB? ObjectBounds
    {
        get
        {
            // A flat volume still covers half a unit on each side of its flat axes
            var volume = GetVolume();
            var size = volume.Size;
            var flat = new Vector3(size.X == 0f ? 0.5f : 0f, size.Y == 0f ? 0.5f : 0f, size.Z == 0f ? 0.5f : 0f);

            return new AABB(volume.Min - flat, volume.Max + flat).Transform(Owner.Transform);
        }
    }

    // The volume the decal is projected through, in the space of the entity
    private AABB GetVolume()
    {
        var width = GetFloatKey("width");
        var height = GetFloatKey("height");
        var depth = GetFloatKey("depth");
        var centerU = GetFloatKey("centeru");
        var centerV = GetFloatKey("centerv");

        return new AABB(
            new Vector3(-width * centerU, -height * centerV, -depth * 0.5f),
            new Vector3(width - centerU * width, height - centerV * height, depth * 0.5f));
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        var (mins, maxs) = (GetVolume().Min, GetVolume().Max);

        context.PushTransform(Owner.Transform);
        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = BoundsColor;
        context.DrawWireBox(mins, maxs);
        context.PopRenderMode();
        context.PopTransform();
    }

    private float GetSize(string key) => Owner.KeyValues.ContainsKey(key) ? GetFloatKey(key) : DefaultSize;
}
