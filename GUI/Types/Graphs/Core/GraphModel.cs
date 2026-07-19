namespace GUI.Types.Graphs.Core;

interface IGraphElement
{
}

/// <summary>
/// Renderer-agnostic node description. Content is an ordered list of rows (sockets and text);
/// all colors are expressed as <see cref="GraphHue"/> slots resolved by the palette at draw time.
/// </summary>
class GraphNode : IGraphElement
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }

    /// <summary>Header band hue. When null, the hue of the first output (or first input) socket is used.</summary>
    public GraphHue? Category { get; set; }

    /// <summary>Optional hue blended into the body fill to mark special nodes.</summary>
    public GraphHue? BodyTint { get; set; }

    /// <summary>Hidden nodes (and their wires) are skipped by rendering, hit testing and bounds.</summary>
    public bool Hidden { get; set; }

    /// <summary>Resource path (without compiled suffix) opened on double click.</summary>
    public string? ExternalResourceName { get; set; }

    public object? Tag { get; set; }

    public List<GraphRow> Rows { get; } = [];
    public List<GraphSocket> Inputs { get; } = [];
    public List<GraphSocket> Outputs { get; } = [];

    public Vector2 Position { get; set; }

    internal Vector2 Size;
    internal bool GeometryDirty = true;

    public GraphSocket AddInput(string name, GraphHue hue, bool allowMultiple = true)
    {
        var socket = new GraphSocket(this, name, hue, isInput: true, allowMultiple);
        Inputs.Add(socket);
        Rows.Add(new SocketRow(socket));
        GeometryDirty = true;
        return socket;
    }

    public GraphSocket AddOutput(string name, GraphHue hue)
    {
        var socket = new GraphSocket(this, name, hue, isInput: false, allowMultiple: true);
        Outputs.Add(socket);
        Rows.Add(new SocketRow(socket));
        GeometryDirty = true;
        return socket;
    }

    public void AddText(string text)
    {
        Rows.Add(new TextRow(text, message: false));
        GeometryDirty = true;
    }

    public void AddSpace() => AddText(string.Empty);

    public void AddMessage(string text)
    {
        Rows.Add(new TextRow(text, message: true));
        GeometryDirty = true;
    }

    public void AddResourceRow(string text, string icon, GraphHue hue)
    {
        Rows.Add(new ResourceRow(text, icon, hue));
        GeometryDirty = true;
    }

    public GraphHue EffectiveCategory => Category
        ?? (Outputs.Count > 0 ? Outputs[0].Hue : Inputs.Count > 0 ? Inputs[0].Hue : GraphHue.Neutral);
}

abstract class GraphRow
{
    internal float CenterOffsetY;
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

sealed class ResourceRow(string text, string icon, GraphHue hue) : GraphRow
{
    public string Text { get; } = text;
    public string Icon { get; } = icon;
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

    internal Vector2 PivotOffset;
    public Vector2 Pivot => Owner.Position + PivotOffset;
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
