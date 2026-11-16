using ValveKeyValue;

namespace ValveResourceFormat.Graphs;

/// <summary>A flow connection from an outflow of one editor node into an inflow of another.</summary>
/// <param name="Id">Editor node id of the connection itself.</param>
/// <param name="Outflow">Outflow of the source node.</param>
/// <param name="Target">Editor node id the flow continues at.</param>
/// <param name="Inflow">Inflow of <paramref name="Target"/>.</param>
internal readonly record struct PulseOutflowLink(int Id, string Outflow, int Target, string Inflow);

/// <summary>A value connection from an output port of one editor node into an input port of another.</summary>
/// <param name="Id">Editor node id of the connection itself.</param>
/// <param name="SourceNode">Editor node id that computes the value.</param>
/// <param name="SourcePort">Output port of <paramref name="SourceNode"/>.</param>
/// <param name="ThisPort">Input port of the reading node.</param>
internal readonly record struct PulseInparamLink(int Id, int SourceNode, string SourcePort, string ThisPort);

/// <summary>What a <see cref="PulseVariableLink"/> reads or writes.</summary>
internal enum PulseVariableKind
{
    /// <summary>An entry of <c>m_Vars</c>, by its editor node id.</summary>
    Variable,

    /// <summary>A temporary variable, by the editor node id of the node declaring it.</summary>
    TempVar,

    /// <summary>An entry of <c>m_BlackboardReferences</c>, by its index.</summary>
    BlackboardReference,
}

/// <summary>A read or write of a variable by an editor node.</summary>
/// <param name="Kind">What <paramref name="Target"/> refers to.</param>
/// <param name="Target">Editor node id, or index for a blackboard reference.</param>
/// <param name="Port">Input port the value is read through, null for a write.</param>
internal readonly record struct PulseVariableLink(PulseVariableKind Kind, int Target, string? Port);

/// <summary>One node of the editor document a compiled Pulse graph was built from.</summary>
internal sealed class PulseEditorNode(int id, string className)
{
    public int Id { get; } = id;

    /// <summary>The document class, or the instruction code for a node <see cref="Instruction"/> is set on.</summary>
    public string ClassName { get; } = className;

    public string? Name { get; set; }

    /// <summary>The node's own document keys, with input ports as <c>m_ValueSource</c> tables.</summary>
    public KVObject Fields { get; } = KVObject.Collection();

    public bool IsFlow { get; init; } = true;
    public bool IsEntry { get; set; }

    /// <summary>The cell class the node was compiled to, null for nodes with no cell.</summary>
    public string? CellClass { get; set; }

    /// <summary>The instruction of a node the document has no class for, null for every other node.</summary>
    public PulseInstructionCode? Instruction { get; set; }

    /// <summary>Top-left corner in the document.</summary>
    public Vector2 Position { get; set; }

    /// <summary>The inflow other flows always connect into, null takes it from the sequence point the cursor arrived at.</summary>
    public string? InflowName { get; set; }

    /// <summary>The inflow used when the cursor arrived at a sequence point that names no inflow.</summary>
    public string FallbackInflowName { get; set; } = "InDefault";

    private readonly List<PulseOutflowLink> outflows = [];
    private readonly List<PulseInparamLink> inparams = [];
    private readonly List<PulseVariableLink> variableLinks = [];

    public IReadOnlyList<PulseOutflowLink> Outflows => outflows;
    public IReadOnlyList<PulseInparamLink> Inparams => inparams;

    /// <summary>Variables the node reads or writes, in the order its fields refer to them.</summary>
    public IReadOnlyList<PulseVariableLink> VariableLinks => variableLinks;

    public void AddVariableRead(PulseVariableKind kind, int target, string port) => variableLinks.Add(new PulseVariableLink(kind, target, port));

    public void AddVariableWrite(PulseVariableKind kind, int target) => variableLinks.Add(new PulseVariableLink(kind, target, null));

    public void AddOutflowConnection(int connectionId, string outflow, int target, string inflow)
    {
        if (outflows.Exists(existing => existing.Outflow == outflow && existing.Target == target && existing.Inflow == inflow))
        {
            return;
        }

        outflows.Add(new PulseOutflowLink(connectionId, outflow, target, inflow));
    }

    /// <summary>Connects an input port, replacing any connection it already had.</summary>
    public void AddInparamConnection(int connectionId, int sourceNode, string sourcePort, string thisPort)
    {
        inparams.RemoveAll(inparam => inparam.ThisPort == thisPort);
        inparams.Add(new PulseInparamLink(connectionId, sourceNode, sourcePort, thisPort));
    }

    public void RenumberInparamConnections(Dictionary<int, int> newIds)
    {
        for (var i = 0; i < inparams.Count; i++)
        {
            if (newIds.TryGetValue(inparams[i].Id, out var newId))
            {
                inparams[i] = inparams[i] with { Id = newId };
            }
        }
    }
}
