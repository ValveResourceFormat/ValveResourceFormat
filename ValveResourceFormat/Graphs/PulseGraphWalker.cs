using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Graphs;

/// <summary>A flow into a chunk the walk could not follow, because the chunk lacks debug info.</summary>
/// <param name="SourceNode">Editor node the flow leaves.</param>
/// <param name="Outflow">Outflow of <paramref name="SourceNode"/> the flow leaves through.</param>
/// <param name="Chunk">The chunk the flow enters.</param>
internal readonly record struct PulseUnwalkedFlow(int SourceNode, string Outflow, int Chunk);

/// <summary>
/// Rebuilds the editor nodes of a compiled Pulse graph. Every instruction keeps the editor node it came from and the
/// sequence point it ran at, so each chunk is walked the way the cursor would, creating one node per editor node and
/// connecting them in the order the flow reaches them.
/// </summary>
internal sealed partial class PulseGraphWalker
{
    public PulseGraphDefinition Definition { get; }

    /// <summary>Editor nodes in the order the walk created them.</summary>
    public IReadOnlyList<PulseEditorNode> Nodes => nodeOrder;

    /// <summary>Editor node id of each entry of <c>m_Vars</c>.</summary>
    public IReadOnlyList<int> VariableNodeIds => variableNodeIds;

    /// <summary>Editor node id of each entry of <c>m_PublicOutputs</c>.</summary>
    public IReadOnlyList<int> PublicOutputNodeIds => publicOutputNodeIds;

    /// <summary>Editor node id of the document root, allocated after every other node.</summary>
    public int RootNodeId { get; private set; }

    public IReadOnlyList<PulseUnwalkedFlow> UnwalkedFlows => unwalkedFlows;

    // Editor node ids already present in the compiled graph, fresh ids are allocated past them
    private readonly HashSet<int> usedNodeIds = [];
    private int nextNodeId = 1;

    private readonly int[] cellNodeIds;
    private readonly int[] variableNodeIds;
    private readonly int[] publicOutputNodeIds;

    private readonly Dictionary<int, PulseEditorNode> nodes = [];
    private readonly List<PulseEditorNode> nodeOrder = [];
    private readonly Dictionary<(int Node, string Port), PulseValueType> outputTypes = [];
    private readonly List<PulseUnwalkedFlow> unwalkedFlows = [];

    public PulseGraphWalker(PulseGraphDefinition definition)
    {
        Definition = definition;

        CollectUsedNodeIds();

        cellNodeIds = [.. definition.Cells.Select(cell => KnownOrFreshNodeId(cell.GetInt32Property("m_nEditorNodeID", -1)))];
        variableNodeIds = [.. definition.Vars.Select(variable => KnownOrFreshNodeId(variable.GetInt32Property("m_nEditorNodeID", -1)))];
        publicOutputNodeIds = [.. definition.PublicOutputs.Select(_ => AllocateNodeId())];
    }

    public bool TryGetNode(int id, out PulseEditorNode node) => nodes.TryGetValue(id, out node!);

    /// <summary>The index into <c>m_PublicOutputs</c> of a public output's editor node id, or -1.</summary>
    public int PublicOutputIndex(int nodeId) => Array.IndexOf(publicOutputNodeIds, nodeId);

    /// <summary>The type of a node's output port, as the register it was written to holds it.</summary>
    public PulseValueType OutputType(int node, string port) => outputTypes.GetValueOrDefault((node, port), PulseValueType.PVAL_VOID);

    /// <summary>Walks every entry point and builds the editor nodes.</summary>
    public void Walk()
    {
        for (var cellIndex = 0; cellIndex < Definition.Cells.Count; cellIndex++)
        {
            if (Definition.Cells[cellIndex].ContainsKey("m_EntryChunk"))
            {
                AddEntryNode(cellIndex);
            }
        }

        WalkAllPaths();

        foreach (var nodeId in fireAndForgetNodes)
        {
            if (nodes.TryGetValue(nodeId, out var node))
            {
                node.Fields.Add("m_nStandardYieldingFlow_CallMode", "ASYNC_FIRE_AND_FORGET");
            }
        }

        OrderOutparamConnectionIds();
        RootNodeId = AllocateNodeId();
    }

    private void Report(string message) => Definition.Report(message);

    private void CollectUsedNodeIds()
    {
        void Use(int id)
        {
            if (id >= 0)
            {
                usedNodeIds.Add(id);
            }
        }

        foreach (var cell in Definition.Cells)
        {
            Use(cell.GetInt32Property("m_nEditorNodeID", -1));
        }

        foreach (var variable in Definition.Vars)
        {
            Use(variable.GetInt32Property("m_nEditorNodeID", -1));
        }

        foreach (var callInfo in Definition.CallInfos)
        {
            Use(callInfo.EditorNode);
            Use(callInfo.CallMethodId);
        }

        foreach (var chunk in Definition.Chunks)
        {
            foreach (var debugInfo in chunk.DebugInfos)
            {
                Use(debugInfo.FlowNode);
                Use(debugInfo.ValueNode);
            }

            foreach (var register in chunk.Registers)
            {
                Use(register.OriginNode);
            }
        }

        nextNodeId = usedNodeIds.Count > 0 ? usedNodeIds.Max() + 1 : 1;
    }

    private int AllocateNodeId()
    {
        while (!usedNodeIds.Add(nextNodeId))
        {
            nextNodeId++;
        }

        return nextNodeId++;
    }

    private int KnownOrFreshNodeId(int id) => id >= 0 ? id : AllocateNodeId();

    // The compiler numbers the outparam registers of an entry node by the ids of the connections reading them
    private void OrderOutparamConnectionIds()
    {
        foreach (var (sourceNode, registers) in entryOutparamRegisters)
        {
            var connections = new List<PulseInparamLink>();

            foreach (var node in nodeOrder)
            {
                foreach (var inparam in node.Inparams)
                {
                    if (inparam.SourceNode == sourceNode)
                    {
                        connections.Add(inparam);
                    }
                }
            }

            var ids = connections.Select(connection => connection.Id).Order().ToList();

            connections.Sort((a, b) =>
            {
                var byRegister = registers.GetValueOrDefault(a.SourcePort, int.MaxValue).CompareTo(registers.GetValueOrDefault(b.SourcePort, int.MaxValue));
                return byRegister != 0 ? byRegister : a.Id.CompareTo(b.Id);
            });

            var newIds = new Dictionary<int, int>(connections.Count);

            for (var i = 0; i < connections.Count; i++)
            {
                newIds[connections[i].Id] = ids[i];
            }

            foreach (var node in nodeOrder)
            {
                node.RenumberInparamConnections(newIds);
            }
        }
    }

    /// <summary>What a register holds at some point of the flow, as the document would express it.</summary>
    private abstract record ValueRef;

    private sealed record LiteralValue(KVObject Value, string Type) : ValueRef;

    private sealed record DomainValue(KVObject Value) : ValueRef;

    private sealed record VariableValue(int VariableNodeId) : ValueRef;

    private sealed record NodeOutput(int NodeId, string Port) : ValueRef;

    // A temporary variable, read by its name, with the node declaring it or -1
    private sealed record LocalNameValue(string Name, int DeclaringNode) : ValueRef;

    // A branch condition the node inverts itself rather than reading from a separate Not node
    private sealed record InvertedValue(ValueRef Inner) : ValueRef;

    // Bookkeeping a flow node computes for itself, such as a loop's bounds checks
    private sealed record InternalValue(string Port, ValueRef? First, ValueRef? Second) : ValueRef;

    // One case test of a switch: the switched on value against a case value
    private sealed record CaseCondition(ValueRef SwitchValue, ValueRef CaseValue, PulseValueType SwitchType) : ValueRef;

    private sealed record UnknownValue : ValueRef;

    /// <summary>
    /// Writes an input port onto <paramref name="container"/>. Values produced by other nodes are wired with an inparam
    /// connection on the consuming node, everything else is stored inline on the port.
    /// </summary>
    /// <param name="node">The node owning the port.</param>
    /// <param name="container">Where the port is stored, the node's fields or a table of ports.</param>
    /// <param name="key">The key of the port in <paramref name="container"/>.</param>
    /// <param name="portName">The port's name, as connections refer to it.</param>
    /// <param name="value">The value the port reads.</param>
    /// <param name="refineLiteralType">Whether a literal must state its type, for ports that take any type.</param>
    private void SetInputPort(PulseEditorNode node, KVObject container, string key, string portName, ValueRef value, bool refineLiteralType = false)
    {
        var port = KVObject.Collection();

        switch (value)
        {
            case LiteralValue literal:
                port.Add("m_ValueSource", "literal");
                port.Add("m_LiteralValue", literal.Value);

                if (refineLiteralType)
                {
                    port.Add("m_UserRefinedType", literal.Type);
                }

                break;

            case DomainValue domainValue:
                port.Add("m_ValueSource", "domain_value");
                port.Add("m_LiteralValue", domainValue.Value);
                break;

            case VariableValue variable:
                port.Add("m_ValueSource", "var");
                port.Add("m_nVarOrConstID", variable.VariableNodeId);
                node.AddVariableRead(PulseVariableKind.Variable, variable.VariableNodeId, key);
                break;

            case LocalNameValue local:
                port.Add("m_ValueSource", "local_name_ref");
                port.Add("m_LiteralValue", local.Name);

                if (local.DeclaringNode >= 0)
                {
                    node.AddVariableRead(PulseVariableKind.TempVar, local.DeclaringNode, key);
                }

                break;

            case NodeOutput output:
                port.Add("m_ValueSource", "port");
                node.AddInparamConnection(AllocateNodeId(), output.NodeId, output.Port, portName);
                break;

            case InvertedValue inverted:
                Report($"Node {node.Id} reads an inverted value through '{portName}' that could not be expressed, the inversion is lost.");
                SetInputPort(node, container, key, portName, inverted.Inner);
                return;

            default:
                Report($"Node {node.Id} port '{portName}' reads a value that could not be resolved.");
                port.Add("m_ValueSource", "literal");
                break;
        }

        container[key] = port;
    }

    private PulseEditorNode AddNode(int id, string className, bool isFlow)
    {
        var node = new PulseEditorNode(id, className) { IsFlow = isFlow };
        nodes.Add(id, node);
        nodeOrder.Add(node);
        usedNodeIds.Add(id);
        return node;
    }

    private void ConnectFlow(int sourceId, string outflow, int targetId, string arrivalSequencePoint)
    {
        if (!nodes.TryGetValue(sourceId, out var source) || !nodes.TryGetValue(targetId, out var target))
        {
            pendingFlowConnections.Add((sourceId, outflow, targetId, arrivalSequencePoint));
            return;
        }

        var inflow = target.InflowName ?? (IsSequencePointName(arrivalSequencePoint) ? target.FallbackInflowName : arrivalSequencePoint);
        source.AddOutflowConnection(AllocateNodeId(), outflow, targetId, inflow);
    }

    // Connections made before the target node knew its type, resolved once the walk is done
    private readonly List<(int Source, string Outflow, int Target, string ArrivalSequencePoint)> pendingFlowConnections = [];

    private void ResolvePendingFlowConnections()
    {
        var pending = pendingFlowConnections.ToList();
        pendingFlowConnections.Clear();

        AddSkippedEntFires();

        // A node that compiled to nothing, such as a skipped one, is left out and the flow through it is joined up
        var passedThrough = pending.Where(connection => !nodes.ContainsKey(connection.Source)).ToLookup(connection => connection.Source);

        IEnumerable<(int Target, string Arrival)> ResolveTargets(int target, string arrival, HashSet<int> seen)
        {
            if (nodes.ContainsKey(target))
            {
                return [(target, arrival)];
            }

            if (!seen.Add(target))
            {
                return [];
            }

            Report($"Node {target} compiled to no instructions, it is left out of the document.");
            return passedThrough[target].SelectMany(next => ResolveTargets(next.Target, next.ArrivalSequencePoint, seen)).ToList();
        }

        foreach (var (source, outflow, target, arrival) in pending)
        {
            if (!nodes.ContainsKey(source))
            {
                continue;
            }

            foreach (var (resolvedTarget, resolvedArrival) in ResolveTargets(target, arrival, []))
            {
                ConnectFlow(source, outflow, resolvedTarget, resolvedArrival);
            }
        }
    }

    // A skipped node compiles to nothing, but a skipped entity fire still lists its output connection
    private void AddSkippedEntFires()
    {
        foreach (var connection in Definition.OutputConnections)
        {
            if (connection.EntFireNode >= 0 && !nodes.ContainsKey(connection.EntFireNode))
            {
                AddSkippedEntFire(connection);
            }
        }
    }

    private void AddSkippedEntFire(PulseOutputConnection connection)
    {
        // The only sequence point left of the node is its outflow, so the cursor seems to arrive there
        var node = AddNode(connection.EntFireNode, "Step_EntFire", isFlow: true);
        node.InflowName = "InDefault";
        node.CellClass = "CPulseCell_Step_EntFire";

        SetInputPort(node, node.Fields, "m_TargetName", "TargetName", new DomainValue(connection.TargetEntity));
        node.Fields.Add("m_Input", connection.TargetInput);

        if (!string.IsNullOrEmpty(connection.Param))
        {
            SetInputPort(node, node.Fields, "m_Param", "pParam", new LiteralValue(string.Empty, "PVAL_STRING"));
            node.Fields.Add("m_SingleParamType", "PVAL_STRING");
            Report($"Node {node.Id} is a skipped entity fire, its parameter value is not kept.");
        }

        node.Fields.Add("m_bNodeSkip", true);
    }

    /// <summary>
    /// Sequence points mark where in a node the cursor is, as opposed to the named outflows it leaves through. They are
    /// matched by the <c>m_&lt;name&gt;Point</c> shape most of them have. <c>InDefault</c> is the default inflow, which
    /// generic cells name after their method.
    /// </summary>
    private static bool IsSequencePointName(string? name)
        => string.IsNullOrEmpty(name)
            || name is "InDefault" or "m_OptionalRequirementsCheckPointForInlineSkip"
            || (name.StartsWith("m_", StringComparison.Ordinal) && name.EndsWith("Point", StringComparison.Ordinal));
}
