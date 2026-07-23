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

    /// <summary>
    /// Slash separated path of the authored containers this node sits in, outermost first,
    /// or null for a node at the graph root. Editor animation graphs nest node managers in
    /// groups and compiled AG2 graphs carry the same shape in m_nodePaths.
    /// </summary>
    public string? GroupPath { get; set; }

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

    /// <summary>
    /// Nodes one wire away, walking the inputs upstream or the outputs downstream. Repeats a
    /// neighbour once per wire that reaches it.
    /// </summary>
    public IEnumerable<GraphNode> Neighbors(bool upstream)
    {
        foreach (var socket in upstream ? Inputs : Outputs)
        {
            foreach (var wire in socket.Wires)
            {
                yield return upstream ? wire.From.Owner : wire.To.Owner;
            }
        }
    }

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

    /// <summary>The named input, added with the given hue if the node does not have one yet.</summary>
    public GraphSocket GetOrAddInput(string name, GraphHue hue, bool allowMultiple = true)
        => Inputs.Find(socket => socket.Name == name) ?? AddInput(name, hue, allowMultiple);

    /// <summary>The named output, added with the given hue if the node does not have one yet.</summary>
    public GraphSocket GetOrAddOutput(string name, GraphHue hue)
        => Outputs.Find(socket => socket.Name == name) ?? AddOutput(name, hue);

    public void AddText(string text)
    {
        Rows.Add(new TextRow(text, message: false));
        ContentVersion++;
    }

    /// <summary>Removes a socket and its row, e.g. after its last wire was disconnected.</summary>
    public void RemoveSocket(GraphSocket socket)
    {
        (socket.IsInput ? Inputs : Outputs).Remove(socket);
        Rows.RemoveAll(row => row is SocketRow socketRow && socketRow.Socket == socket);
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

    /// <summary>
    /// Marks this node as referencing an external file: sets the double-click target and adds a
    /// resource row with the asset <paramref name="icon"/> and the file's trimmed display name.
    /// Shared by the graph frontends so a referenced file reads and opens the same way in each.
    /// </summary>
    public void AddResourceReference(string resourcePath, string icon, GraphHue hue)
    {
        ExternalResourceName = resourcePath;
        AddResourceRow(TrimResourceName(resourcePath), icon, hue);
    }

    /// <summary>Basename without extension, capped with a leading ellipsis when long.</summary>
    private static string TrimResourceName(string resourcePath)
    {
        var display = resourcePath;

        var extension = display.LastIndexOf('.');
        if (extension >= 0)
        {
            display = display[..extension];
        }

        var lastSlash = display.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            display = display[(lastSlash + 1)..];
        }

        return display.Length > 23 ? '…' + display[^22..] : display;
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

/// <summary>Which nodes around a target an isolate command keeps visible.</summary>
enum GraphIsolateMode
{
    /// <summary>The transitive upstream and downstream chain of the node.</summary>
    Chain,

    /// <summary>Everything that can reach the node.</summary>
    Upstream,

    /// <summary>Everything the node can reach.</summary>
    Downstream,

    /// <summary>The node's authored group, nested sub-groups included.</summary>
    Group,

    /// <summary>The connected component the node sits in.</summary>
    Island,
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
