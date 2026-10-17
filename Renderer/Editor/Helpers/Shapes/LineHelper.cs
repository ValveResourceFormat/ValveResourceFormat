using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// Lines from the entities one key of an entity names to the entity, or to the entities another key names.
/// They are drawn in the draw color of the entity, and in the selection color while it is selected, leaving out
/// the entities that are hidden. The color the declaration gives is only for views from above, which are not
/// drawn.
/// </summary>
internal sealed class LineHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    /// <inheritdoc/>
    public override bool RequiresSelection => IsSelectedOnly;

    private const string NameKey = "targetname";

    private readonly string startKey;
    private readonly string startValueKey;
    private readonly string? endKey;
    private readonly string? endValueKey;
    private readonly List<(IHelperOwner Start, IHelperOwner End)> segments = [];
    private int startCount;

    /// <summary>Gets whether the lines are only drawn while the entity is selected.</summary>
    private bool IsSelectedOnly { get; }

    private LineHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        IsSelectedOnly = declaration.Name.Equals("selected_line", StringComparison.OrdinalIgnoreCase);

        startKey = GetArgument(3)!;
        startValueKey = GetArgument(4)!;

        if (GetArgument(5) is { } key && GetArgument(6) is { } valueKey)
        {
            endKey = key;
            endValueKey = valueKey;
        }
    }

    /// <summary>Creates the lines a class declares.</summary>
    /// <returns>The lines, or <see langword="null"/> when the declaration does not say what they go to.</returns>
    public static LineHelper? Create(IHelperOwner owner, HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count > 4 ? new LineHelper(owner, declaration) : null;
    }

    /// <inheritdoc/>
    public override void Link()
    {
        segments.Clear();

        var starts = Find(startKey, startValueKey);
        var ends = endKey != null && endValueKey != null ? Find(endKey, endValueKey) : [Owner];

        startCount = starts.Count;

        foreach (var end in ends)
        {
            foreach (var start in starts)
            {
                segments.Add((start, end));
            }
        }

        if (!IsSelectedOnly)
        {
            AddShapes();
        }
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (IsSelectedOnly && !state.IsSelected)
        {
            return;
        }

        // Lines from more than one entity are told apart from a single one while selected
        var color = !state.IsSelected ? GetDrawColor(state) : startCount > 1 ? SelectionOverlayColor : SelectionColor;

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.LineWidth = 2f;

        foreach (var (start, end) in segments)
        {
            // Whether an entity is hidden is only known as the selection is drawn, each frame; the lines drawn
            // while the entity is not selected are drawn once
            if (state.IsSelected && (!IsShown(start) || !IsShown(end)))
            {
                continue;
            }

            context.DrawLine(start.Transform.Translation, end.Transform.Translation, color);
        }

        context.LineWidth = 0f;
        context.PopRenderMode();
    }

    // An entity is hidden with its layer, and one in no layer is never hidden
    private static bool IsShown(IHelperOwner entity) => entity.LayerName.Length == 0 || entity.Scene.IsLayerEnabled(entity.LayerName);

    // Entities are found by name, or by any other key, where the first that has the value is taken
    private IReadOnlyList<IHelperOwner> Find(string key, string valueKey)
    {
        if (Owner.KeyValues.GetStringProperty(valueKey) is not { Length: > 0 } value)
        {
            return [];
        }

        if (key.Equals(NameKey, StringComparison.OrdinalIgnoreCase))
        {
            return Owner.FindByName(value);
        }

        return Owner.FindByKey(key, value) is { } found ? [found] : [];
    }
}
