using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A label written at a point one key of an entity holds, in the space of the entity. It is written while the
/// entity is selected, and while it is not where the class asks for that.
/// </summary>
public sealed class TextHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    private const string DefaultPointKey = "origin";

    private bool? modelIsBreakable;

    /// <summary>Gets the key of the entity that holds the point.</summary>
    public string PointKey { get; }

    /// <summary>Gets the label.</summary>
    public string Text { get; }

    /// <summary>Gets whether the label is written while the entity is not selected too.</summary>
    public bool DrawUnselected { get; }

    /// <summary>Gets whether no label is written while the point is the origin of the entity.</summary>
    public bool HideIfZero { get; }

    /// <summary>Gets whether the label is only written while the model of the entity can break into pieces.</summary>
    public bool RequiresBreakableModel { get; }

    /// <summary>Initializes the label a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public TextHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        // A color can follow the label, but the label is always written in white
        PointKey = GetArgument(0)?.ToLowerInvariant() ?? DefaultPointKey;
        Text = GetArgument(1) ?? string.Empty;
        DrawUnselected = HasArgument("draw_unselected");
        HideIfZero = HasArgument("hideifzero");
        RequiresBreakableModel = HasArgument("requires_breakable_model");
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (DrawUnselected)
        {
            AddShapes();
        }
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected && !DrawUnselected)
        {
            return;
        }

        if (VecLineHelper.GetPoint(Owner, GetStringKey(PointKey), isLocal: true, HideIfZero) is not { } point
            || (RequiresBreakableModel && !(modelIsBreakable ??= VecLineHelper.HasBreakableModel(Owner))))
        {
            return;
        }

        context.DrawText(Text, point, Color32.White);
    }
}
