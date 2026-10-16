using System.Linq;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// Lines from the entities one key of an entity names to the entity, or to the entities another key names.
/// They are drawn in the color of its class, and in the selection color while it is selected. The color the
/// declaration gives is only for views from above, which are not drawn.
/// </summary>
public sealed class LineHelper : EntityHelper
{
    private const string NameKey = "targetname";

    private static readonly Color32 SelectedColor = new(255, 255, 0);

    // Lines from more than one entity are told apart from a single one while selected
    private static readonly Color32 SelectedManyColor = new(255, 183, 45);

    private readonly string startKey;
    private readonly string startValueKey;
    private readonly string? endKey;
    private readonly string? endValueKey;
    private readonly List<(Vector3 Start, Vector3 End)> segments = [];
    private int startCount;

    /// <summary>Gets whether the lines are only drawn while the entity is selected.</summary>
    public bool IsSelectedOnly { get; }

    private LineHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
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
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <returns>The lines, or <see langword="null"/> when the declaration does not say what they go to.</returns>
    public static LineHelper? Create(IHelperOwner owner, FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count > 4 ? new LineHelper(owner, declaration) : null;
    }

    /// <inheritdoc/>
    public override void Link()
    {
        segments.Clear();

        var starts = Find(startKey, startValueKey);
        var ends = endKey != null && endValueKey != null
            ? Find(endKey, endValueKey).Select(static end => end.Transform.Translation).ToList()
            : [Owner.Transform.Translation];

        startCount = starts.Count;

        foreach (var start in starts)
        {
            foreach (var end in ends)
            {
                segments.Add((start.Transform.Translation, end));

                if (!IsSelectedOnly)
                {
                    AddLine(start.Transform.Translation, end);
                }
            }
        }
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        var lines = shapes.World(HelperPasses.Visible);
        var color = startCount > 1 ? SelectedManyColor : SelectedColor;

        foreach (var (start, end) in segments)
        {
            lines.AddLine(start, end, color);
        }
    }

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

    private void AddLine(Vector3 start, Vector3 end)
    {
        var origin = (start + end) / 2f;
        var color = ClassColor;

        Owner.AddNode(new LineSceneNode(Owner.Scene, start - origin, end - origin, color, color)
        {
            LayerName = HammerEntityVisuals.MarkerLayerName,
            Transform = Matrix4x4.CreateTranslation(origin),
        });
    }
}
