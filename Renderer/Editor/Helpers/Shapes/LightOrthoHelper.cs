using System.Globalization;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The box an orthographic light shines through while it is selected, as wide and high as two of its keys
/// say and reaching forward as far as its range, in the color of the light and faintly over everything.
/// </summary>
internal sealed class LightOrthoHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        var halfWidth = GetFloatKey("ortholightwidth") * 0.5f;
        var halfHeight = GetFloatKey("ortholightheight") * 0.5f;
        var (mins, maxs) = (new Vector3(0f, -halfWidth, -halfHeight), new Vector3(GetFloatKey("range"), halfWidth, halfHeight));
        var color = GetColor();

        context.PushTransform(Owner.RigidTransform);

        context.PushRenderMode(ToolRenderMode.WireframeTranslucentNoZ);
        context.DrawColor = color with { A = 64 };
        context.DrawWireBox(mins, maxs);
        context.PopRenderMode();

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = color;
        context.DrawWireBox(mins, maxs);
        context.PopRenderMode();

        context.PopTransform();
    }

    // Three or four numbers, opaque where the fourth is left out, and none at all until the key is set
    private Color32 GetColor()
    {
        Span<byte> channels = [0, 0, 0, 255];
        var parts = GetStringKey("color").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return default;
        }

        for (var i = 0; i < Math.Min(parts.Length, channels.Length); i++)
        {
            channels[i] = byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel) ? channel : (byte)0;
        }

        return new Color32(channels[0], channels[1], channels[2], channels[3]);
    }
}
