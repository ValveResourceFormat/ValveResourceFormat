using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;

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
        var color = ValueParsing.TryParseColor(GetStringKey("color"), out var keyColor) ? keyColor : default;

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
}
