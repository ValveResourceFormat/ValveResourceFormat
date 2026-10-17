using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A point one key of an entity holds, marked by a handle and joined to the entity by a line, the point in
/// world space or in the space of the entity. It is drawn while the entity is selected, the line dashed where
/// the scene hides it, and faintly while it is not where the class asks for that.
/// </summary>
internal sealed class VecLineHelper : PointKeyHelper
{
    /// <summary>Gets the color of the line.</summary>
    private Color32 Color { get; }

    public VecLineHelper(IHelperOwner owner, HelperDeclaration declaration)
        : base(owner, declaration, isLocal: declaration.Name.Equals("vecline_local", StringComparison.OrdinalIgnoreCase))
    {
        Color = declaration.Arguments.Count >= 4 ? GetColorArguments(1) : Color32.White;
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (GetPoint(state) is not { } point)
        {
            return;
        }

        var origin = Owner.RigidTransform.Translation;
        var color = Color with { A = state.IsSelected ? byte.MaxValue : (byte)128 };

        // The point is a handle, over everything while the entity is selected
        context.LayerOverride = state.IsSelected ? ToolRenderLayer.Overlay0 : null;
        context.DrawHandle(10f, ToolHandleType.Circle, color, point, noZ: state.IsSelected, border: false, translucent: true);
        context.LayerOverride = null;

        if (state.IsSelected)
        {
            DrawLineOverHidden(context, point, origin, color);
            return;
        }

        context.PushRenderMode(ToolRenderMode.WireframeTranslucent);
        context.DrawLine(point, origin, color);
        context.PopRenderMode();
    }
}
