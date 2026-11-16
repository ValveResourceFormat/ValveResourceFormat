using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Builds the node graph of a compiled Pulse graph: one card per editor node, wired by its flow and value
/// connections, with a hub per variable that reads and writes link to.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulseGraphDef">CPulseGraphDef</seealso>
internal sealed class PulseGraphBuilder
{
    /// <summary>Receives diagnostics about parts of the definition that could not be read cleanly.</summary>
    public IProgress<string>? ProgressReporter { get; set; }

    private readonly KVObject? data;
    private PulseGraphWalker? walker;

    private readonly Dictionary<int, Node> cards = [];
    private readonly Dictionary<int, Node> variableHubs = [];
    private readonly Dictionary<int, Node> blackboardReferenceHubs = [];
    private readonly HashSet<string> reportedUnknownCellClasses = [];

    private const string VariableHubType = "Variable";
    private const string BlackboardReferenceHubType = "Blackboard Reference";

    /// <summary>Creates a builder over one compiled Pulse graph.</summary>
    /// <param name="data">The compiled graph definition to read.</param>
    public PulseGraphBuilder(KVObject data)
    {
        this.data = data;
    }

    /// <summary>Creates a builder over a graph that was already walked.</summary>
    /// <param name="walker">The walked graph.</param>
    public PulseGraphBuilder(PulseGraphWalker walker)
    {
        this.walker = walker;
    }

    /// <summary>Fills <paramref name="document"/> with the graph and lays it out.</summary>
    /// <param name="document">The graph to fill.</param>
    public void Build(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (walker == null)
        {
            walker = new PulseGraphWalker(new PulseGraphDefinition(data!, ProgressReporter));
            walker.Walk();
        }

        var definition = walker.Definition;

        for (var i = 0; i < walker.VariableNodeIds.Count; i++)
        {
            variableHubs.TryAdd(walker.VariableNodeIds[i], AddVariableHub(document, definition.Vars[i]));
        }

        var inflows = walker.Nodes.SelectMany(node => node.Outflows).ToLookup(outflow => outflow.Target, outflow => outflow.Inflow);

        foreach (var node in walker.Nodes)
        {
            cards[node.Id] = AddCard(document, node, inflows[node.Id]);
        }

        foreach (var node in walker.Nodes)
        {
            ConnectNode(document, node, cards[node.Id]);
        }

        AddUnwalkedChunks(document);
        AddGraphInfo(document, definition.Data);

        document.LayoutNodesPacked();
        document.Legend.AddRange(PulseHues.Legend());
    }

    private Node AddCard(GraphDocument document, PulseEditorNode node, IEnumerable<string> inflows)
    {
        var card = CreateNode(Title(node), Subtitle(node), Category(node));
        card.Tag = node;

        foreach (var inflow in inflows)
        {
            card.FlowIn(inflow);
        }

        AddFieldRows(card, node);

        if (node.ClassName == "Step_PublicOutput")
        {
            AddPublicOutputRows(card, node);
        }

        foreach (var inparam in node.Inparams)
        {
            card.ValueIn(inparam.ThisPort, HueOf(walker!.OutputType(inparam.SourceNode, inparam.SourcePort)));
        }

        foreach (var outflow in node.Outflows)
        {
            card.FlowOut(outflow.Outflow);
        }

        return document.AddNode(card);
    }

    private void ConnectNode(GraphDocument document, PulseEditorNode node, Node card)
    {
        foreach (var outflow in node.Outflows)
        {
            if (cards.TryGetValue(outflow.Target, out var target))
            {
                document.Connect(card.FlowOut(outflow.Outflow), target.FlowIn(outflow.Inflow));
            }
        }

        foreach (var inparam in node.Inparams)
        {
            if (cards.TryGetValue(inparam.SourceNode, out var source))
            {
                var hue = HueOf(walker!.OutputType(inparam.SourceNode, inparam.SourcePort));
                document.Connect(source.ValueOut(inparam.SourcePort, hue), card.ValueIn(inparam.ThisPort, hue));
            }
        }

        foreach (var link in node.VariableLinks)
        {
            var variable = link.Kind switch
            {
                PulseVariableKind.Variable => variableHubs.GetValueOrDefault(link.Target),
                PulseVariableKind.TempVar => cards.GetValueOrDefault(link.Target),
                _ => BlackboardReferenceHub(document, link.Target),
            };

            if (variable == null || variable == card)
            {
                continue;
            }

            if (link.Port is { } port)
            {
                LinkRead(document, variable, card, port);
            }
            else
            {
                LinkWrite(document, card, variable);
            }
        }
    }

    private static void LinkRead(GraphDocument document, Node source, Node reader, string port)
        => document.Connect(source.GetOrAddOutput("reads", PulseHues.VariableLinkHue), reader.AddInput(port, PulseHues.VariableLinkHue), dashed: true);

    private static void LinkWrite(GraphDocument document, Node writer, Node target)
        => document.Connect(writer.GetOrAddOutput("var", PulseHues.VariableLinkHue), target.GetOrAddInput("writes", PulseHues.VariableLinkHue), dashed: true);

    // Every input port of a node, including those in its tables of ports
    private static IEnumerable<(string Key, KVObject Port)> Ports(PulseEditorNode node)
    {
        foreach (var (key, value) in node.Fields)
        {
            if (IsPort(value))
            {
                yield return (key, value);
            }
            else if (key is "m_GenericPorts" or "m_ArgList" or "m_ReturnValues" && value.IsCollection)
            {
                foreach (var (portKey, port) in value)
                {
                    if (IsPort(port))
                    {
                        yield return (portKey, port);
                    }
                }
            }
        }
    }

    private static bool IsPort(KVObject value) => value.IsCollection && value.ContainsKey("m_ValueSource");

    // Keys that are drawn as the title, as links or by their own rows
    private static readonly HashSet<string> HiddenFields =
        ["m_GenericNodeType", "m_VarDefID", "m_SourceID", "m_OutputDefID", "m_nBlackboardReferenceIdx", "m_EventDefinitions", "m_Args", "m_ReturnValues"];

    private static void AddFieldRows(Node card, PulseEditorNode node)
    {
        foreach (var (key, port) in Ports(node))
        {
            var source = port.GetStringProperty("m_ValueSource", string.Empty);
            if (source is "literal" or "domain_value")
            {
                card.AddText(port.TryGetValue("m_LiteralValue", out var literal)
                    ? $"{key} = {KVGraphNode.StringifyValue(literal)}"
                    : $"{key} = <unresolved>");
            }
        }

        foreach (var (key, value) in node.Fields)
        {
            if (HiddenFields.Contains(key))
            {
                continue;
            }

            if (key == "m_GenericKeys")
            {
                foreach (var (settingKey, setting) in value)
                {
                    // Custom attributes hold the setting under the same name
                    var settingValue = setting.IsCollection && setting.ContainsKey(settingKey) ? setting[settingKey] : setting;
                    AddValueRow(card, settingKey, settingValue);
                }

                continue;
            }

            AddValueRow(card, key, value);
        }

        if (node.Fields.TryGetValue("m_EventDefinitions", out var events))
        {
            foreach (var timelineEvent in events.AsArraySpan())
            {
                var seconds = timelineEvent.GetFloatProperty("m_flTimeFromPrevious");
                card.AddText(string.Create(CultureInfo.InvariantCulture, $"{timelineEvent.GetStringProperty("m_InternalPortName")} after {seconds}s"));
            }
        }
    }

    // Scalars and arrays of scalars, the rest of a cell is its compiled bookkeeping
    private static void AddValueRow(Node card, string key, KVObject value)
    {
        if (value.IsCollection)
        {
            return;
        }

        if (value.IsArray)
        {
            foreach (var element in value.AsArraySpan())
            {
                if (element.IsCollection || element.IsArray)
                {
                    return;
                }
            }
        }

        card.AddText($"{key} = {KVGraphNode.StringifyValue(value)}");
    }

    private void AddPublicOutputRows(Node card, PulseEditorNode node)
    {
        var definition = walker!.Definition;
        var outputIndex = walker.PublicOutputIndex(node.Fields.GetInt32Property("m_OutputDefID", -1));

        if (outputIndex < 0)
        {
            return;
        }

        var publicOutput = definition.PublicOutputs[outputIndex];
        var outputName = publicOutput.GetStringProperty("m_Name", "<NAME UNKNOWN>");

        card.AddText($"Public Output: {outputName}");
        card.AddText($"Description: {publicOutput.GetStringProperty("m_Description", string.Empty)}");

        // Entity I/O the graph fires when this output triggers
        foreach (var connection in definition.OutputConnections)
        {
            if (connection.SourceOutput != outputName)
            {
                continue;
            }

            var target = $"{connection.TargetEntity}.{connection.TargetInput}";
            card.AddText(string.IsNullOrEmpty(connection.Param) ? $"Fires {target}" : $"Fires {target}({connection.Param})");
        }
    }

    private static string Title(PulseEditorNode node)
    {
        if (node.Instruction is { } code)
        {
            return code switch
            {
                PulseInstructionCode.SET_VAR_ARRAY_ELEMENT_1D => "Set Array Element",
                PulseInstructionCode.GET_ARRAY_ELEMENT => "Get Array Element",
                PulseInstructionCode.ELEMENT_ACCESS => "Element Access",
                PulseInstructionCode.GET_BLACKBOARD_REFERENCE => "Get Blackboard Reference",
                PulseInstructionCode.SET_BLACKBOARD_REFERENCE => "Set Blackboard Reference",
                _ => node.ClassName,
            };
        }

        if (node.ClassName is "Flow_Generic" or "Value_Generic")
        {
            var nodeType = node.Fields.GetStringProperty("m_GenericNodeType", string.Empty);
            return nodeType == node.CellClass ? CellName(nodeType) : nodeType;
        }

        if (node.ClassName == "Inflow_Method")
        {
            return node.Name ?? "Method";
        }

        var separator = node.ClassName.IndexOf('_', StringComparison.Ordinal);
        if (separator >= 0)
        {
            return node.ClassName[(separator + 1)..];
        }

        return node.ClassName.StartsWith("Expr", StringComparison.Ordinal) ? node.ClassName[4..] : node.ClassName;
    }

    private static string Subtitle(PulseEditorNode node) => node switch
    {
        { Instruction: not null } => "Instruction",
        { ClassName: "Inflow_Method" } => "Method",
        { ClassName: "Flow_Generic" or "Value_Generic", CellClass: { } cellClass } => cellClass,
        { ClassName: "Flow_Generic" or "Value_Generic" } => "Function",
        _ => node.ClassName,
    };

    // The last part of a cell class name, e.g. "Wait" for CPulseCell_Inflow_Wait
    private static string CellName(string cellClass)
    {
        var index = cellClass.LastIndexOf('_', StringComparison.Ordinal);
        return index == -1 ? cellClass : cellClass[(index + 1)..];
    }

    private PulseCategory Category(PulseEditorNode node)
    {
        if (node.Instruction != null)
        {
            return PulseCategory.Instruction;
        }

        if (node.CellClass is { } cellClass)
        {
            var category = PulseHues.CategoryOf(cellClass);

            if (category == PulseCategory.Other && reportedUnknownCellClasses.Add(cellClass))
            {
                ProgressReporter?.Report($"Unknown pulse cell class \"{cellClass}\".");
            }

            return category;
        }

        if (node.IsEntry)
        {
            return PulseCategory.EntryPoint;
        }

        return node.ClassName switch
        {
            "Step_CallMethod" or "Flow_Generic" or "Value_Generic" => PulseCategory.Call,
            "Value_VarReference" => PulseCategory.Variable,
            _ when node.ClassName.StartsWith("Outflow_", StringComparison.Ordinal) => PulseCategory.FlowControl,
            _ when node.ClassName.StartsWith("Step_", StringComparison.Ordinal) => PulseCategory.Step,
            _ => PulseCategory.Instruction,
        };
    }

    // Definitions share the hub their reads and writes link to, since no pane displays them
    private static Node AddVariableHub(GraphDocument document, KVObject variable)
    {
        var node = CreateNode(variable.GetStringProperty("m_Name", string.Empty), VariableHubType, PulseCategory.Variable);
        node.AddText($"Type: {variable.GetStringProperty("m_Type")}");
        node.AddText($"Initial value: {variable["m_DefaultValue"]}");
        node.AddText($"Keys source: {variable.GetStringProperty("m_nKeysSource")}");

        if (variable.GetBooleanProperty("m_bIsObservable"))
        {
            node.AddText("Observable");
        }

        if (variable.GetBooleanProperty("m_bIsPublicBlackboardVariable"))
        {
            node.AddText("Public blackboard variable");
        }

        var description = variable.GetStringProperty("m_Description");
        if (!string.IsNullOrEmpty(description))
        {
            node.AddText(description);
        }

        return document.AddNode(node);
    }

    private Node BlackboardReferenceHub(GraphDocument document, int index)
    {
        if (blackboardReferenceHubs.TryGetValue(index, out var hub))
        {
            return hub;
        }

        var references = walker!.Definition.BlackboardReferences;
        var name = index >= 0 && index < references.Count
            ? references[index].GetStringProperty("m_NodeName", string.Create(CultureInfo.InvariantCulture, $"<BLACKBOARD REFERENCE {index}>"))
            : string.Create(CultureInfo.InvariantCulture, $"<UNKNOWN m_nBlackboardReferenceIdx={index}>");

        hub = document.AddNode(CreateNode(name, BlackboardReferenceHubType, PulseCategory.Variable));
        blackboardReferenceHubs[index] = hub;
        return hub;
    }

    // Chunks without debug info have no editor nodes to rebuild, so their instructions are drawn in order
    private void AddUnwalkedChunks(GraphDocument document)
    {
        var heads = new Dictionary<int, Node>();

        foreach (var flow in walker!.UnwalkedFlows)
        {
            if (!heads.TryGetValue(flow.Chunk, out var head))
            {
                head = AddInstructionList(document, flow.Chunk);
                heads[flow.Chunk] = head;
            }

            if (cards.TryGetValue(flow.SourceNode, out var source))
            {
                document.Connect(source.FlowOut(flow.Outflow), head.FlowIn("InDefault"));
            }
        }
    }

    private Node AddInstructionList(GraphDocument document, int chunkIndex)
    {
        ProgressReporter?.Report($"Chunk {chunkIndex} has no debug info for some of its instructions, it is drawn as a list of instructions.");

        var head = CreateNode(string.Create(CultureInfo.InvariantCulture, $"Chunk {chunkIndex}"), "Chunk", PulseCategory.FlowControl);
        head.AddMessage("No debug info, the editor nodes are unknown");
        document.AddNode(head);

        var previous = head;

        foreach (var instruction in walker!.Definition.Chunks[chunkIndex].Instructions)
        {
            var card = CreateNode(instruction.CodeName, "Instruction", PulseCategory.Instruction);

            foreach (var (name, value) in (ReadOnlySpan<(string, int)>)[
                ("m_nReg0", instruction.Reg0), ("m_nReg1", instruction.Reg1), ("m_nReg2", instruction.Reg2),
                ("m_nVar", instruction.Var), ("m_nChunk", instruction.Chunk), ("m_nDestInstruction", instruction.DestInstruction),
                ("m_nInvokeBindingIndex", instruction.InvokeBindingIndex), ("m_nCallInfoIndex", instruction.CallInfoIndex),
                ("m_nConstIdx", instruction.ConstIdx), ("m_nDomainValueIdx", instruction.DomainValueIdx),
                ("m_nBlackboardReferenceIdx", instruction.BlackboardReferenceIdx), ("m_nTempVarIdx", instruction.TempVarIdx)])
            {
                if (value >= 0)
                {
                    card.AddText(string.Create(CultureInfo.InvariantCulture, $"{name} = {value}"));
                }
            }

            document.AddNode(card);
            document.Connect(previous.FlowOut("OutDefault"), card.FlowIn("InDefault"));
            previous = card;
        }

        return head;
    }

    private static void AddGraphInfo(GraphDocument document, KVObject definition)
    {
        var graphInfoNode = CreateNode("Graph info", string.Empty, PulseCategory.Other);

        // Friendlier names for some graph keys, the others display under their raw name
        Dictionary<string, string> prettyNameMap = new()
        {
            { "m_DomainIdentifier", "Domain" },
            { "m_DomainSubType", "Domain sub-type" },
            { "m_ParentMapName", "Parent map name" },
            { "m_ParentXmlName", "Parent XML panel" },
        };

        foreach (var (key, value) in definition)
        {
            if (value.IsArray || value.IsCollection || value.IsNull)
            {
                continue;
            }

            graphInfoNode.AddText($"{prettyNameMap.GetValueOrDefault(key, key)}: {value}");
        }

        document.AddNode(graphInfoNode);
    }

    private static GraphHue HueOf(PulseValueType valueType) => PulseHues.HueOf(valueType switch
    {
        PulseValueType.PVAL_INT or PulseValueType.PVAL_FLOAT
            or PulseValueType.PVAL_TYPESAFE_INT or PulseValueType.PVAL_TYPESAFE_INT64
            or PulseValueType.PVAL_GAMETIME => PulseValueKind.Number,
        PulseValueType.PVAL_STRING => PulseValueKind.String,
        PulseValueType.PVAL_BOOL => PulseValueKind.Bool,
        PulseValueType.PVAL_CURSOR_FLOW => PulseValueKind.Flow,
        _ => PulseValueKind.Other,
    });

    private static Node CreateNode(string name, string nodeType, PulseCategory category) => new()
    {
        Name = name,
        NodeType = nodeType,
        Category = PulseHues.HueOf(category),
    };

    private sealed class Node : KVGraphNode
    {
        private readonly Dictionary<string, GraphSocket> flowInputs = [];
        private readonly Dictionary<string, GraphSocket> flowOutputs = [];
        private readonly Dictionary<string, GraphSocket> valueInputs = [];
        private readonly Dictionary<string, GraphSocket> valueOutputs = [];

        private static GraphHue FlowHue => PulseHues.HueOf(PulseValueKind.Flow);

        public GraphSocket FlowIn(string name)
            => flowInputs.TryGetValue(name, out var socket) ? socket : flowInputs[name] = AddInput(FlowLabel(name), FlowHue);

        public GraphSocket FlowOut(string name)
            => flowOutputs.TryGetValue(name, out var socket) ? socket : flowOutputs[name] = AddOutput(FlowLabel(name), FlowHue);

        public GraphSocket ValueIn(string name, GraphHue hue)
            => valueInputs.TryGetValue(name, out var socket) ? socket : valueInputs[name] = AddInput(name, hue);

        public GraphSocket ValueOut(string name, GraphHue hue)
            => valueOutputs.TryGetValue(name, out var socket) ? socket : valueOutputs[name] = AddOutput(name, hue);

        // The default inflow and outflow are the plain sequence a step continues through
        private static string FlowLabel(string name) => name switch
        {
            "InDefault" or "OutDefault" => string.Empty,
            _ when name.StartsWith("m_", StringComparison.Ordinal) => name[2..],
            _ => name,
        };
    }
}
