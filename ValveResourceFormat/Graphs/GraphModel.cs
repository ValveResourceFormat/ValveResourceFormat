namespace ValveResourceFormat.Graphs;

/// <summary>
/// Renderer-agnostic node description: pure content with no derived geometry. Content is an
/// ordered list of rows (sockets and text); all colors are expressed as <see cref="GraphHue"/>
/// slots the host resolves at draw time. <see cref="Position"/> is document state (the layout's
/// or the user's placement); everything measured lives in <see cref="GraphGeometry"/>.
/// </summary>
public class GraphNode
{
    private string title = string.Empty;
    private string? subtitle;

    /// <summary>Name drawn in the node's header band.</summary>
    public string Title
    {
        get => title;
        set
        {
            title = value;
            ContentVersion++;
        }
    }

    /// <summary>Secondary name drawn beside the title, usually the node's type.</summary>
    public string? Subtitle
    {
        get => subtitle;
        set
        {
            subtitle = value;
            ContentVersion++;
        }
    }

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
    /// Key the host resolves to an icon image. Nodes with one get a neutral left gutter holding
    /// the icon.
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

    /// <summary>Free slot for the frontend that built this node.</summary>
    public object? Tag { get; set; }

    /// <summary>Content rows in the order they are drawn.</summary>
    public List<GraphRow> Rows { get; } = [];

    /// <summary>Input sockets, in the order they were added.</summary>
    public List<GraphSocket> Inputs { get; } = [];

    /// <summary>Output sockets, in the order they were added.</summary>
    public List<GraphSocket> Outputs { get; } = [];

    /// <summary>
    /// Nodes one wire away, walking the inputs upstream or the outputs downstream. Repeats a
    /// neighbour once per wire that reaches it.
    /// </summary>
    /// <param name="upstream">Whether to walk the inputs rather than the outputs.</param>
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

    /// <summary>Top-left corner of the node on the canvas.</summary>
    public Vector2 Position { get; set; }

    /// <summary>Bumped by every content mutation; views compare it against their measured geometry.</summary>
    public int ContentVersion { get; private set; }

    /// <summary>Adds an input socket and the row that carries it.</summary>
    /// <param name="name">Socket name, drawn beside the dot.</param>
    /// <param name="hue">Colour slot of the socket and the wires reaching it.</param>
    /// <param name="allowMultiple">Whether more than one wire may land here.</param>
    public GraphSocket AddInput(string name, GraphHue hue, bool allowMultiple = true)
    {
        var socket = new GraphSocket(this, name, hue, isInput: true, allowMultiple);
        Inputs.Add(socket);
        Rows.Add(new SocketRow(socket));
        ContentVersion++;
        return socket;
    }

    /// <summary>Adds an output socket and the row that carries it.</summary>
    /// <param name="name">Socket name, drawn beside the dot.</param>
    /// <param name="hue">Colour slot of the socket and the wires leaving it.</param>
    public GraphSocket AddOutput(string name, GraphHue hue)
    {
        var socket = new GraphSocket(this, name, hue, isInput: false, allowMultiple: true);
        Outputs.Add(socket);
        Rows.Add(new SocketRow(socket));
        ContentVersion++;
        return socket;
    }

    /// <summary>The named input, added with the given hue if the node does not have one yet.</summary>
    /// <param name="name">Socket name to find or create.</param>
    /// <param name="hue">Colour slot used if it has to be created.</param>
    /// <param name="allowMultiple">Whether more than one wire may land here.</param>
    public GraphSocket GetOrAddInput(string name, GraphHue hue, bool allowMultiple = true)
        => Inputs.Find(socket => socket.Name == name) ?? AddInput(name, hue, allowMultiple);

    /// <summary>The named output, added with the given hue if the node does not have one yet.</summary>
    /// <param name="name">Socket name to find or create.</param>
    /// <param name="hue">Colour slot used if it has to be created.</param>
    public GraphSocket GetOrAddOutput(string name, GraphHue hue)
        => Outputs.Find(socket => socket.Name == name) ?? AddOutput(name, hue);

    /// <summary>Adds a plain text row.</summary>
    /// <param name="text">The text to draw.</param>
    public void AddText(string text)
    {
        Rows.Add(new TextRow(text, message: false));
        ContentVersion++;
    }

    /// <summary>Removes a socket and its row, e.g. after its last wire was disconnected.</summary>
    /// <param name="socket">The socket to remove.</param>
    public void RemoveSocket(GraphSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        (socket.IsInput ? Inputs : Outputs).Remove(socket);
        Rows.RemoveAll(row => row is SocketRow socketRow && socketRow.Socket == socket);
        ContentVersion++;
    }

    /// <summary>Adds an empty row, leaving a gap between the rows around it.</summary>
    public void AddSpace() => AddText(string.Empty);

    /// <summary>Adds a row marked as a message rather than data.</summary>
    /// <param name="text">The message to draw.</param>
    public void AddMessage(string text)
    {
        Rows.Add(new TextRow(text, message: true));
        ContentVersion++;
    }

    /// <summary>Adds a row carrying an icon beside its text.</summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="icon">Icon key the host resolves.</param>
    /// <param name="hue">Colour slot of the row.</param>
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
    /// <param name="resourcePath">Path of the referenced file.</param>
    /// <param name="icon">Icon key the host resolves.</param>
    /// <param name="hue">Colour slot of the row.</param>
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

    /// <summary>Adds a compact hue-marked note row, e.g. an inlined special-target connection.</summary>
    /// <param name="text">The note to draw.</param>
    /// <param name="hue">Colour slot of the note.</param>
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

    /// <summary>The header hue, falling back to the first socket's hue when none was set.</summary>
    public GraphHue EffectiveCategory => Category
        ?? (Outputs.Count > 0 ? Outputs[0].Hue : Inputs.Count > 0 ? Inputs[0].Hue : GraphHue.Neutral);
}

/// <summary>One line of a node's content.</summary>
public abstract class GraphRow
{
}

/// <summary>A row of plain text, or of a message when <see cref="IsMessage"/> is set.</summary>
/// <param name="text">The text to draw.</param>
/// <param name="message">Whether the row is a message rather than data.</param>
public sealed class TextRow(string text, bool message) : GraphRow
{
    /// <summary>The text drawn on this row.</summary>
    public string Text { get; } = text;

    /// <summary>Whether this row is a message rather than data.</summary>
    public bool IsMessage { get; } = message;
}

/// <summary>A row carrying one socket.</summary>
/// <param name="socket">The socket on this row.</param>
public sealed class SocketRow(GraphSocket socket) : GraphRow
{
    /// <summary>The socket drawn on this row.</summary>
    public GraphSocket Socket { get; } = socket;
}

/// <summary>A row carrying an input on the left edge and an output on the right.</summary>
/// <param name="input">The input socket, or null.</param>
/// <param name="output">The output socket, or null.</param>
public sealed class PairedSocketRow(GraphSocket? input, GraphSocket? output) : GraphRow
{
    /// <summary>The socket on the left edge, if any.</summary>
    public GraphSocket? Input { get; } = input;

    /// <summary>The socket on the right edge, if any.</summary>
    public GraphSocket? Output { get; } = output;
}

/// <summary>A row naming a referenced file, drawn with an asset icon.</summary>
/// <param name="text">Display name of the file.</param>
/// <param name="icon">Icon key the host resolves.</param>
/// <param name="hue">Colour slot of the row.</param>
public sealed class ResourceRow(string text, string icon, GraphHue hue) : GraphRow
{
    /// <summary>Display name of the referenced file.</summary>
    public string Text { get; } = text;

    /// <summary>Icon key the host resolves to an image.</summary>
    public string Icon { get; } = icon;

    /// <summary>Colour slot of the row.</summary>
    public GraphHue Hue { get; } = hue;
}

/// <summary>A compact hue-marked note row.</summary>
/// <param name="text">The note to draw.</param>
/// <param name="hue">Colour slot of the note.</param>
public sealed class AnnotationRow(string text, GraphHue hue) : GraphRow
{
    /// <summary>The note drawn on this row.</summary>
    public string Text { get; } = text;

    /// <summary>Colour slot of the note.</summary>
    public GraphHue Hue { get; } = hue;
}

/// <summary>One endpoint a wire can dock at, owned by a node.</summary>
public class GraphSocket
{
    /// <summary>The node this socket belongs to.</summary>
    public GraphNode Owner { get; }

    /// <summary>Name drawn beside the socket dot; may be empty.</summary>
    public string Name { get; }

    /// <summary>Colour slot of the socket and the wires it carries.</summary>
    public GraphHue Hue { get; }

    /// <summary>Whether this socket takes wires in rather than sending them out.</summary>
    public bool IsInput { get; }

    /// <summary>Whether more than one wire may dock here.</summary>
    public bool AllowMultiple { get; }

    /// <summary>Wires docked at this socket.</summary>
    public List<GraphWire> Wires { get; } = [];

    /// <summary>Whether any wire is docked here.</summary>
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
public enum GraphIsolateMode
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
public enum GraphLegendKind
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
/// <param name="Label">Text of the legend row.</param>
/// <param name="Hue">Colour slot the sample is drawn in.</param>
/// <param name="Kind">How the sample is drawn.</param>
public readonly record struct GraphLegendEntry(string Label, GraphHue Hue, GraphLegendKind Kind = GraphLegendKind.Category);

/// <summary>A directed connection from an output socket to an input socket.</summary>
public class GraphWire
{
    /// <summary>The output socket this wire leaves.</summary>
    public GraphSocket From { get; }

    /// <summary>The input socket this wire enters.</summary>
    public GraphSocket To { get; }

    /// <summary>Whether this is a secondary binding rather than primary flow.</summary>
    public bool Dashed { get; init; }

    /// <summary>Short text drawn at the wire midpoint (e.g. entity I/O delay/parameter).</summary>
    public string? Label { get; set; }

    internal GraphWire(GraphSocket from, GraphSocket to)
    {
        From = from;
        To = to;
    }
}
