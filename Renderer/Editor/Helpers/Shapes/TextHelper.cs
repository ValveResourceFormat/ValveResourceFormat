using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A label written at a point one key of an entity holds, in the space of the entity. It is written while the
/// entity is selected, and while it is not where the class asks for that.
/// </summary>
internal sealed class TextHelper : PointKeyHelper
{
    /// <summary>Gets the label.</summary>
    private string Text { get; }

    public TextHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration, isLocal: true)
    {
        // A color can follow the label, but the label is always written in white
        Text = GetArgument(1) ?? string.Empty;
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (GetPoint(state) is { } point)
        {
            context.DrawText(Text, point, Color32.White);
        }
    }
}
