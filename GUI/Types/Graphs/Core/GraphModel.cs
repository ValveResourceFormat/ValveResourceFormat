using System.Linq;

namespace GUI.Types.Graphs.Core;

interface IGraphElement
{
}

/// <summary>
/// Renderer-agnostic node description: pure content with no derived geometry. Content is an
/// ordered list of rows (sockets and text); all colors are expressed as <see cref="GraphHue"/>
/// slots resolved by the palette at draw time. <see cref="Position"/> is document state (the
/// layout's or the user's placement); everything measured lives in <see cref="GraphGeometry"/>.
/// </summary>
class GraphNode : IGraphElement
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }

    /// <summary>Stable creation order; layout runs in this order, independent of z-order.</summary>
    public int Sequence { get; internal set; }

    /// <summary>Header band hue. When null, the hue of the first output (or first input) socket is used.</summary>
    public GraphHue? Category { get; set; }

    /// <summary>Optional hue blended into the body fill to mark special nodes.</summary>
    public GraphHue? BodyTint { get; set; }

    /// <summary>Hidden nodes (and their wires) are skipped by rendering, hit testing and bounds.</summary>
    public bool Hidden { get; set; }

    /// <summary>Resource path (without compiled suffix) opened on double click.</summary>
    public string? ExternalResourceName { get; set; }

    private string? iconKey;

    /// <summary>
    /// Key resolved through <see cref="GraphView.IconResolver"/> to an icon image.
    /// Nodes with one get a neutral left gutter holding the icon.
    /// </summary>
    public string? IconKey
    {
        get => iconKey;
        set
        {
            iconKey = value;
            ContentVersion++;
        }
    }

    public object? Tag { get; set; }

    public List<GraphRow> Rows { get; } = [];
    public List<GraphSocket> Inputs { get; } = [];
    public List<GraphSocket> Outputs { get; } = [];

    public Vector2 Position { get; set; }

    /// <summary>Bumped by every content mutation; views compare it against their measured geometry.</summary>
    public int ContentVersion { get; private set; }

    public GraphSocket AddInput(string name, GraphHue hue, bool allowMultiple = true)
    {
        var socket = new GraphSocket(this, name, hue, isInput: true, allowMultiple);
        Inputs.Add(socket);
        Rows.Add(new SocketRow(socket));
        ContentVersion++;
        return socket;
    }

    public GraphSocket AddOutput(string name, GraphHue hue)
    {
        var socket = new GraphSocket(this, name, hue, isInput: false, allowMultiple: true);
        Outputs.Add(socket);
        Rows.Add(new SocketRow(socket));
        ContentVersion++;
        return socket;
    }

    public void AddText(string text)
    {
        Rows.Add(new TextRow(text, message: false));
        ContentVersion++;
    }

    public void AddSpace() => AddText(string.Empty);

    public void AddMessage(string text)
    {
        Rows.Add(new TextRow(text, message: true));
        ContentVersion++;
    }

    public void AddResourceRow(string text, string icon, GraphHue hue)
    {
        Rows.Add(new ResourceRow(text, icon, hue));
        ContentVersion++;
    }

    /// <summary>Compact hue-marked note row, e.g. an inlined special-target connection.</summary>
    public void AddAnnotation(string text, GraphHue hue)
    {
        Rows.Add(new AnnotationRow(text, hue));
        ContentVersion++;
    }

    /// <summary>
    /// Replaces the rows with paired lines where input i and output i share a row
    /// (inputs on the left edge, outputs on the right). Drops any non-socket rows.
    /// </summary>
    public void PairSocketRows()
    {
        Rows.Clear();

        var count = Math.Max(Inputs.Count, Outputs.Count);

        for (var i = 0; i < count; i++)
        {
            Rows.Add(new PairedSocketRow(
                i < Inputs.Count ? Inputs[i] : null,
                i < Outputs.Count ? Outputs[i] : null));
        }

        ContentVersion++;
    }

    /// <summary>
    /// Whether the socket order carries meaning from the asset, like a blend weight index or a
    /// declaration order. Nodes that clear this let the layout permute their rows to shorten
    /// the wires; ordered ones are left exactly as the frontend built them.
    /// </summary>
    public bool SocketOrderFixed { get; set; } = true;

    /// <summary>
    /// Sorts both socket sides by <paramref name="key"/> and refills the existing socket rows in
    /// the new order. The row structure is untouched, so interleaved text and annotation rows
    /// stay exactly where the frontend put them. Does nothing while
    /// <see cref="SocketOrderFixed"/> holds. Returns whether the order actually changed.
    /// </summary>
    public bool ReorderSockets(Func<GraphSocket, float> key)
    {
        if (SocketOrderFixed || (Inputs.Count < 2 && Outputs.Count < 2))
        {
            return false;
        }

        // The header hue falls back to the first socket, so a permutation must not repaint the
        // card; pinning the category up front keeps the node looking like itself.
        Category ??= EffectiveCategory;

        if (!(SortStable(Inputs, key) | SortStable(Outputs, key)))
        {
            return false;
        }

        var inputs = new Queue<GraphSocket>(Inputs);
        var outputs = new Queue<GraphSocket>(Outputs);

        for (var i = 0; i < Rows.Count; i++)
        {
            switch (Rows[i])
            {
                case SocketRow row:
                    Rows[i] = new SocketRow(row.Socket.IsInput ? inputs.Dequeue() : outputs.Dequeue());
                    break;

                case PairedSocketRow row:
                    Rows[i] = new PairedSocketRow(
                        row.Input != null ? inputs.Dequeue() : null,
                        row.Output != null ? outputs.Dequeue() : null);
                    break;
            }
        }

        ContentVersion++;
        return true;
    }

    // Sorts every socket by the key, then restores the original relative order of the pinned
    // ones. A pinned pin may therefore move past a free one, which reorders nothing meaningful,
    // but two pinned pins can never trade places, so a True branch stays above its False.
    // OrderBy is stable, so equal keys keep their current order and layout stays reproducible.
    private static bool SortStable(List<GraphSocket> sockets, Func<GraphSocket, float> key)
    {
        if (sockets.Count < 2)
        {
            return false;
        }

        var pinned = sockets.Where(static s => s.OrderFixed).ToList();
        var sorted = sockets.OrderBy(key).ToList();

        if (pinned.Count > 1)
        {
            var slot = 0;

            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].OrderFixed)
                {
                    sorted[i] = pinned[slot++];
                }
            }
        }

        var changed = false;

        for (var i = 0; i < sockets.Count; i++)
        {
            if (sockets[i] != sorted[i])
            {
                changed = true;
                break;
            }
        }

        if (changed)
        {
            sockets.Clear();
            sockets.AddRange(sorted);
        }

        return changed;
    }

    public GraphHue EffectiveCategory => Category
        ?? (Outputs.Count > 0 ? Outputs[0].Hue : Inputs.Count > 0 ? Inputs[0].Hue : GraphHue.Neutral);
}

abstract class GraphRow
{
}

sealed class TextRow(string text, bool message) : GraphRow
{
    public string Text { get; } = text;
    public bool IsMessage { get; } = message;
}

sealed class SocketRow(GraphSocket socket) : GraphRow
{
    public GraphSocket Socket { get; } = socket;
}

sealed class PairedSocketRow(GraphSocket? input, GraphSocket? output) : GraphRow
{
    public GraphSocket? Input { get; } = input;
    public GraphSocket? Output { get; } = output;
}

sealed class ResourceRow(string text, string icon, GraphHue hue) : GraphRow
{
    public string Text { get; } = text;
    public string Icon { get; } = icon;
    public GraphHue Hue { get; } = hue;
}

sealed class AnnotationRow(string text, GraphHue hue) : GraphRow
{
    public string Text { get; } = text;
    public GraphHue Hue { get; } = hue;
}

class GraphSocket : IGraphElement
{
    public GraphNode Owner { get; }
    public string Name { get; }
    public GraphHue Hue { get; }
    public bool IsInput { get; }
    public bool AllowMultiple { get; }

    /// <summary>
    /// Whether this row's place in the node carries meaning. Control flow pins are pinned so a
    /// reorder can never swap a True branch with a False one; value pins are free to move.
    /// </summary>
    public bool OrderFixed { get; set; } = true;

    public List<GraphWire> Wires { get; } = [];

    public bool IsConnected => Wires.Count > 0;

    internal GraphSocket(GraphNode owner, string name, GraphHue hue, bool isInput, bool allowMultiple)
    {
        Owner = owner;
        Name = name;
        Hue = hue;
        IsInput = isInput;
        AllowMultiple = allowMultiple;
    }
}

enum GraphCurveVerb
{
    MoveTo,
    LineTo,
    CubicTo,

    /// <summary>Rational quadratic; represents elliptical arc pieces exactly.</summary>
    ConicTo,
}

/// <summary>
/// One drawing command of a wire's exact routed path. A/B are cubic control points;
/// conics use A as their single control point and Weight as the rational weight.
/// </summary>
readonly record struct GraphCurveCommand(GraphCurveVerb Verb, Vector2 A, Vector2 B, Vector2 End, float Weight)
{
    public static GraphCurveCommand MoveTo(Vector2 end) => new(GraphCurveVerb.MoveTo, end, end, end, 0f);
    public static GraphCurveCommand LineTo(Vector2 end) => new(GraphCurveVerb.LineTo, end, end, end, 0f);
    public static GraphCurveCommand CubicTo(Vector2 c1, Vector2 c2, Vector2 end) => new(GraphCurveVerb.CubicTo, c1, c2, end, 0f);
    public static GraphCurveCommand ConicTo(Vector2 control, Vector2 end, float weight) => new(GraphCurveVerb.ConicTo, control, control, end, weight);
}

/// <summary>How a legend row's color sample is drawn.</summary>
enum GraphLegendKind
{
    /// <summary>Filled swatch in the muted node-header palette.</summary>
    Category,

    /// <summary>Line sample in the bright wire/socket palette.</summary>
    Wire,

    /// <summary>Dashed line sample in the bright wire/socket palette.</summary>
    DashedWire,

    /// <summary>Diamond marker in the bright wire/socket palette.</summary>
    Marker,
}

/// <summary>
/// One legend row. Colors are palette slots, never raw ARGB, so the legend adapts to the
/// active theme like the graph itself; the host resolves them at paint time.
/// </summary>
readonly record struct GraphLegendEntry(string Label, GraphHue Hue, GraphLegendKind Kind = GraphLegendKind.Category);

class GraphWire : IGraphElement
{
    public GraphSocket From { get; }
    public GraphSocket To { get; }
    public bool Dashed { get; init; }

    /// <summary>Short text drawn at the wire midpoint (e.g. entity I/O delay/parameter).</summary>
    public string? Label { get; set; }

    internal GraphWire(GraphSocket from, GraphSocket to)
    {
        From = from;
        To = to;
    }
}
