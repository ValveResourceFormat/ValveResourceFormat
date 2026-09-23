using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Builds the node graph of a compiled Pulse graph: one card per cell, wired by the instruction
/// flow and the register bindings between them.
/// </summary>
internal sealed class PulseGraphBuilder
{
    /// <summary>Receives diagnostics about cells the definition could not be read cleanly from.</summary>
    public IProgress<string>? ProgressReporter { get; set; }

    /// <summary>Creates a builder over one compiled Pulse graph.</summary>
    /// <param name="data">The compiled graph definition to read.</param>
    public PulseGraphBuilder(KVObject data)
    {
        graphDefinition = data;

        cells = graphDefinition.GetArray("m_Cells");
        chunks = graphDefinition.GetArray("m_Chunks");
        invokeBindings = graphDefinition.GetArray("m_InvokeBindings");
        domainValues = graphDefinition.GetArray("m_DomainValues");
        constants = graphDefinition.GetArray("m_Constants");
        variables = graphDefinition.GetArray("m_Vars");
        publicOutputs = graphDefinition.GetArray("m_PublicOutputs");
        callInfos = graphDefinition.GetArray("m_CallInfos") ?? [];
        tempVarBanks = graphDefinition.GetArray("m_TempVarBanks") ?? [];
        blackboardReferences = graphDefinition.GetArray("m_BlackboardReferences") ?? [];
        outputConnections = graphDefinition.GetArray("m_OutputConnections") ?? [];

        cellOutflows = new List<PulseOutflowConnection>?[cells.Count];

        // Loop bodies are calls that carry a break destination
        hasLoopBodyCalls = callInfos.Any(callInfo => callInfo.GetInt32Property("m_nBreakDestChunk", -1) != -1);
    }

    private readonly KVObject graphDefinition;

    // Every serialized instruction code, in order. Typed variants such as ADD_INT only exist at runtime.
    enum InstructionCode
    {
        INVALID,
        IMMEDIATE_HALT,
        RETURN_VOID,
        RETURN_VALUE,
        LOOP_BREAK,
        NOP,
        JUMP,
        JUMP_COND,
        CHUNK_LEAP,
        CHUNK_LEAP_COND,
        PULSE_CALL_SYNC,
        PULSE_CALL_ASYNC_FIRE,
        CREATE_CHILD_CURSOR_OUTFLOW,
        CELL_INVOKE,
        LIBRARY_INVOKE,
        SET_VAR,
        GET_VAR,
        GET_VAR_DETACH,
        DETACH_REGISTER,
        SET_VAR_ARRAY_ELEMENT_1D,
        SET_VAR_OBSERVABLE,
        GET_CONST,
        GET_ARRAY_ELEMENT,
        GET_DOMAIN_VALUE,
        COPY,
        NOT,
        NEGATE,
        ADD,
        SUB,
        MUL,
        DIV,
        MOD,
        LT,
        LTE,
        EQ,
        NE,
        AND,
        OR,
        SCALE,
        SCALE_INV,
        ELEMENT_ACCESS,
        CONVERT_VALUE,
        REINTERPRET_INSTANCE,
        GET_BLACKBOARD_REFERENCE,
        SET_BLACKBOARD_REFERENCE,
        GET_TEMPVAR,
        SET_TEMPVAR,
        SET_TEMPVAR_OBSERVABLE,
    }

    // The full EPulseValueType set (pulse_system). A register names one of these, optionally with
    // a ":subtype" the parser strips. Drives socket/wire colour and is shown on variable nodes.
    enum PulseValueType
    {
        PVAL_INVALID,
        PVAL_VOID,
        PVAL_BOOL,
        PVAL_INT,
        PVAL_FLOAT,
        PVAL_STRING,
        PVAL_VEC2,
        PVAL_VEC3,
        PVAL_VEC3_WORLDSPACE,
        PVAL_VEC4,
        PVAL_QANGLE,
        PVAL_TRANSFORM,
        PVAL_TRANSFORM_WORLDSPACE,
        PVAL_COLOR_RGB,
        PVAL_EHANDLE,
        PVAL_PARTICLE_EHANDLE,
        PVAL_RESOURCE,
        PVAL_RESOURCE_NAME,
        PVAL_SNDEVT_NAME,
        PVAL_SNDEVT_GUID,
        PVAL_ENTITY_NAME,
        PVAL_TYPESAFE_INT,
        PVAL_TYPESAFE_INT64,
        PVAL_GAMETIME,
        PVAL_SCHEMA_ENUM,
        PVAL_PANORAMA_PANEL_HANDLE,
        PVAL_MODEL_MATERIAL_GROUP,
        PVAL_OPAQUE_HANDLE,
        PVAL_TEST_HANDLE,
        PVAL_ARRAY,
        PVAL_VARIANT,
        PVAL_ANY,
        PVAL_CURSOR_FLOW,
        PVAL_UNKNOWN,
        PVAL_ANIM_SEQUENCE,
        PVAL_VDATA_CHOICE,
        PVAL_COUNT,
    }

    private readonly IReadOnlyList<KVObject> cells;
    private readonly IReadOnlyList<KVObject> chunks;
    private readonly IReadOnlyList<KVObject> invokeBindings;
    private readonly IReadOnlyList<KVObject> domainValues;
    private readonly IReadOnlyList<KVObject> constants;
    private readonly IReadOnlyList<KVObject> variables;
    private readonly IReadOnlyList<KVObject> publicOutputs;
    private readonly IReadOnlyList<KVObject> callInfos;
    private readonly IReadOnlyList<KVObject> tempVarBanks;
    private readonly IReadOnlyList<KVObject> blackboardReferences;
    private readonly IReadOnlyList<KVObject> outputConnections;
    private readonly bool hasLoopBodyCalls;
    private readonly List<PulseOutflowConnection>?[] cellOutflows;
    private readonly Dictionary<int, Dictionary<int, int>> registerMentionCounts = [];
    private readonly List<RemoteNodeInfo> remoteNodesToResolve = [];
    private readonly HashSet<string> reportedUnknownCellClasses = [];
    private readonly HashSet<string> reportedUnknownValueTypes = [];

    // Per chunk, the last instruction of each loop keyed by its first
    private Dictionary<int, int>[] loopsByChunk = [];

    // A call or leap node, labelled with its target chunk's name once every chunk is named
    private readonly record struct RemoteNodeInfo(int TargetChunk, Node Node, string TargetNamePrefix);

    // What a register holds while a flow is walked: the socket of the node that computed it, or a
    // constant that is printed inline where it is read. The latest write wins.
    private readonly record struct RegisterValue(GraphSocket? Socket, KVObject? Constant);

    private sealed record PulseOutflowConnection(string SourceOutflowName, int DestChunk, int DestInstructionIdx, KVObject? OutflowRegisterMap)
    {
        public static PulseOutflowConnection? FromKV(KVObject obj)
        {
            if (!obj.TryGetValue("m_SourceOutflowName", out var sourceOutflowName) ||
                !obj.TryGetValue("m_nDestChunk", out var destChunk) ||
                !obj.TryGetValue("m_nInstruction", out var destInstructionIdx))
            {
                return null;
            }

            return new PulseOutflowConnection(
                sourceOutflowName.ToString(CultureInfo.InvariantCulture),
                destChunk.ToInt32(CultureInfo.InvariantCulture),
                destInstructionIdx.ToInt32(CultureInfo.InvariantCulture),
                obj.TryGetValue("m_OutflowRegisterMap", out var outflowRegisterMap) ? outflowRegisterMap : null
            );
        }
    }

    #region Socket types
    // Buckets match the ones the pulse editor itself binds wires by (pulse_scene_styles_v2):
    // string, number, bool, flow, and one "other" bucket for every remaining type.
    private static GraphHue HueOfPval(PulseValueType valueType) => valueType switch
    {
        PulseValueType.PVAL_INT or PulseValueType.PVAL_FLOAT
            or PulseValueType.PVAL_TYPESAFE_INT or PulseValueType.PVAL_TYPESAFE_INT64
            or PulseValueType.PVAL_GAMETIME => GraphHue.Amber,
        PulseValueType.PVAL_STRING => GraphHue.Green,
        PulseValueType.PVAL_BOOL => GraphHue.Orange,
        PulseValueType.PVAL_CURSOR_FLOW => GraphHue.Neutral,
        _ => GraphHue.Teal,
    };
    #endregion Socket types

    private static Node CreateNode(string name, string nodeType, PulseCategory category, KVObject? data = null) => new(data)
    {
        Name = name,
        NodeType = nodeType,
        Category = PulseHues.HueOf(category),
    };

    private bool TryAddRegisterMapOutParams(
        Node node,
        int chunkIndex,
        Dictionary<int, RegisterValue> registerValues,
        KVObject registerMap)
    {
        var outParams = registerMap["m_Outparams"];
        if (outParams.IsNull)
        {
            return false;
        }

        foreach (var (paramName, regIdx) in outParams)
        {
            var regValue = (int)regIdx;
            registerValues[regValue] = new(node.CreateSocketOutFromValueType(paramName, GetValueTypeFromRegister(chunkIndex, regValue)), null);
        }

        return true;
    }

    // Shows the cell's own settings, leaving out internal fields
    private void AddFilteredCellDetails(Node node, int cellIndex)
    {
        foreach (var (key, value) in cells[cellIndex])
        {
            if (key is "_class" or "m_EntryChunk" or "m_nEditorNodeID" || value.IsCollection || value.IsArray)
            {
                continue;
            }

            node.AddText($"{key} = {KVGraphNode.StringifyValue(value)}");
        }
    }

    // Adds a flow output to node and draws the flow it leads to. The outflow's own out params are
    // registers of the destination chunk, set when the flow starts there.
    private void TraverseOutflow(
        GraphDocument document,
        Node node,
        string socketLabel,
        int sourceChunk,
        int destChunk,
        int destInstructionIdx,
        int maxInstructionIdx, // non-inclusive, use when there's a need to limit the range inside a loop
        Dictionary<int, RegisterValue> registerValues,
        KVObject? outflowRegisterMap = null)
    {
        // Register numbers and instruction ranges belong to one chunk, so a flow into another chunk
        // starts with no registers computed and no range limit
        var isSameChunk = destChunk == sourceChunk;
        var destRegisterValues = isSameChunk ? new Dictionary<int, RegisterValue>(registerValues) : [];

        if (outflowRegisterMap is not null)
        {
            TryAddRegisterMapOutParams(node, destChunk, destRegisterValues, outflowRegisterMap);
        }

        var outputSocket = node.CreateFlowOut(socketLabel);
        if (destChunk == -1)
        {
            return;
        }

        TraverseNodesForChunk(
            document,
            destChunk,
            FlowContinuation.Of(outputSocket),
            destRegisterValues,
            Math.Max(0, destInstructionIdx),
            isSameChunk ? maxInstructionIdx : int.MaxValue
        );
    }

    private void AddOutflowSocket(
        GraphDocument document,
        Node node,
        PulseOutflowConnection outflow,
        string socketLabel,
        int sourceChunk,
        Dictionary<int, RegisterValue> registerValues,
        int maxInstructionIdx
    )
    {
        if (outflow.DestChunk == -1 || outflow.DestInstructionIdx == -1)
        {
            return;
        }

        TraverseOutflow(document, node, socketLabel, sourceChunk, outflow.DestChunk, outflow.DestInstructionIdx, maxInstructionIdx,
            registerValues, outflow.OutflowRegisterMap);
    }

    private PulseCategory GetCellCategory(int cellIdx)
    {
        var className = cells[cellIdx].GetStringProperty("_class");
        var category = PulseHues.CategoryOf(className);

        if (category == PulseCategory.Other && reportedUnknownCellClasses.Add(className))
        {
            ProgressReporter?.Report($"Unknown pulse cell class \"{className}\".");
        }

        return category;
    }

    // The last part of the class name, e.g. "Wait" for CPulseCell_Inflow_Wait
    private string GetCellName(int cellIdx)
    {
        var className = cells[cellIdx].GetStringProperty("_class");
        var index = className.LastIndexOf('_');
        return index == -1 ? "Unknown" : className[(index + 1)..];
    }

    private static InstructionCode GetInstructionType(KVObject instruction)
        => Enum.TryParse(instruction.GetStringProperty("m_nCode"), out InstructionCode code) ? code : InstructionCode.INVALID;

    private PulseValueType GetValueTypeFromRegister(int chunkIdx, int regIdx)
    {
        var regInfo = chunks[chunkIdx].GetArray("m_Registers")[regIdx];
        return ParseValueType(regInfo.GetStringProperty("m_Type"));
    }

    /// <summary>
    /// Reads a register or variable type name. Handle and enum types name their subject after a
    /// colon (PVAL_EHANDLE:func_button), which is not part of the type itself.
    /// </summary>
    private PulseValueType ParseValueType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return PulseValueType.PVAL_VOID;
        }

        var subtype = typeName.IndexOf(':', StringComparison.Ordinal);
        var baseType = subtype >= 0 ? typeName[..subtype] : typeName;

        if (Enum.TryParse(baseType, out PulseValueType valueType))
        {
            return valueType;
        }

        if (reportedUnknownValueTypes.Add(baseType))
        {
            ProgressReporter?.Report($"Unknown pulse value type \"{baseType}\".");
        }

        return PulseValueType.PVAL_VOID;
    }

    private const string VariableHubType = "Variable";
    private const string TempVariableHubType = "Temporary Variable";
    private const string BlackboardReferenceHubType = "Blackboard Reference";

    private readonly Dictionary<(string HubType, int Bank, int Index), Node> variableHubs = [];

    // One hub node per variable; writes wire into it and reads out of it, so a variable's reads and
    // writes are visible connectivity. Temporaries live in a bank named by the chunk that uses them,
    // so the same index means different values in different banks.
    private Node VariableHubFor(GraphDocument document, string hubType, int bankIndex, int index, string name, KVObject? data)
    {
        if (!variableHubs.TryGetValue((hubType, bankIndex, index), out var node))
        {
            node = CreateNode(name, hubType, PulseCategory.Variable, data);
            document.AddNode(node);
            variableHubs[(hubType, bankIndex, index)] = node;
        }

        return node;
    }

    private void AddVariableRead(
        GraphDocument document,
        Node hub,
        string title,
        string name,
        int chunkIndex,
        int regIndex,
        Dictionary<int, RegisterValue> registerValues)
    {
        var node = CreateNode(title, "Instruction", PulseCategory.Instruction);

        node.AddText(name);
        registerValues[regIndex] = new(node.CreateSocketOutFromValueType("retval", GetValueTypeFromRegister(chunkIndex, regIndex)), null);
        document.AddNode(node);

        var readsOutput = hub.GetOrAddOutput("reads", PulseHues.VariableLinkHue);
        document.Connect(readsOutput, node.AddInput("var", PulseHues.VariableLinkHue, allowMultiple: true), dashed: true);
    }

    private FlowContinuation AddVariableWrite(
        GraphDocument document,
        Node hub,
        string title,
        string name,
        int chunkIndex,
        int regIndex,
        FlowContinuation previousActionOutSocket,
        Dictionary<int, RegisterValue> registerValues,
        int arrayIndexRegIndex = -1)
    {
        var node = CreateNode(title, "Instruction", PulseCategory.Instruction);
        var nextActionOutSocket = CreateSequentialActionSockets(document, node, previousActionOutSocket);

        node.AddText(name);
        if (arrayIndexRegIndex != -1)
        {
            AddNodeRegisterInput(document, node, chunkIndex, registerValues, arrayIndexRegIndex, "index");
        }
        AddNodeRegisterInput(document, node, chunkIndex, registerValues, regIndex, "value");
        document.AddNode(node);

        var writesInput = hub.GetOrAddInput("writes", PulseHues.VariableLinkHue);
        document.Connect(node.AddOutput("var", PulseHues.VariableLinkHue), writesInput, dashed: true);

        return nextActionOutSocket;
    }

    private Node VariableHubFromInstruction(GraphDocument document, KVObject instruction, out string name)
    {
        var varIndex = instruction.GetInt32Property("m_nVar");
        var variable = variables.ElementAtOrDefault(varIndex);
        if (variable == null)
        {
            name = $"<UNKNOWN m_nVar={varIndex}>";
            ProgressReporter?.Report($"Failed to retrieve variable name of ID={varIndex}. Invalid graph definition?");
        }
        else
        {
            name = variable.GetStringProperty("m_Name");
        }

        return VariableHubFor(document, VariableHubType, -1, varIndex, name, variable);
    }

    private Node TempVariableHubFromInstruction(GraphDocument document, int chunkIndex, KVObject instruction, out string name)
    {
        var bankIndex = chunks[chunkIndex].GetInt32Property("m_nTempVarBank", -1);
        var tempVarIndex = instruction.GetInt32Property("m_nTempVarIdx", -1);
        var tempVar = tempVarBanks.ElementAtOrDefault(bankIndex)?.GetArray("m_TempVars")?.ElementAtOrDefault(tempVarIndex);
        if (tempVar == null)
        {
            name = $"<UNKNOWN m_nTempVarIdx={tempVarIndex}>";
            ProgressReporter?.Report($"Failed to retrieve temporary variable {tempVarIndex} from bank {bankIndex}. Invalid graph definition?");
        }
        else
        {
            name = tempVar.GetStringProperty("m_Name");
        }

        return VariableHubFor(document, TempVariableHubType, bankIndex, tempVarIndex, name, null);
    }

    private Node BlackboardReferenceHubFromInstruction(GraphDocument document, KVObject instruction, out string name)
    {
        var referenceIndex = instruction.GetInt32Property("m_nBlackboardReferenceIdx", -1);
        var reference = blackboardReferences.ElementAtOrDefault(referenceIndex);
        if (reference == null)
        {
            name = $"<UNKNOWN m_nBlackboardReferenceIdx={referenceIndex}>";
            ProgressReporter?.Report($"Failed to retrieve blackboard reference of ID={referenceIndex}. Invalid graph definition?");
        }
        else
        {
            name = reference.GetStringProperty("m_NodeName", $"<BLACKBOARD REFERENCE {referenceIndex}>");
        }

        return VariableHubFor(document, BlackboardReferenceHubType, -1, referenceIndex, name, reference);
    }

    /// <summary>The port a flow continues into, created on first use.</summary>
    private sealed class FlowContinuation(Func<GraphSocket> create)
    {
        private GraphSocket? socket;

        /// <summary>A port the node only grows once a flow continues into it.</summary>
        /// <param name="node">The node the port would belong to.</param>
        public static FlowContinuation Pending(Node node) => new(() => node.CreateFlowOut(""));

        /// <summary>A port that already exists, such as a named loop or branch outflow.</summary>
        /// <param name="existing">The port to continue from.</param>
        public static FlowContinuation Of(GraphSocket existing) => new(() => existing);

        /// <summary>The port itself.</summary>
        public GraphSocket Socket => socket ??= create();
    }

    private static FlowContinuation CreateSequentialActionSockets(GraphDocument document, Node node, FlowContinuation previousActionOutSocket)
    {
        var socketIn = node.CreateFlowIn("");
        document.Connect(previousActionOutSocket.Socket, socketIn);

        return FlowContinuation.Pending(node);
    }

    private void AddNodeRegisterInput(GraphDocument document,
        Node node,
        int chunkIndex,
        Dictionary<int, RegisterValue> registerValues,
        int regIndex,
        string name)
    {
        if (!registerValues.TryGetValue(regIndex, out var value))
        {
            node.AddText($"{name} = <FAILED TO RESOLVE>");
            ProgressReporter?.Report($"Failed to find register id={regIndex} at chunk={chunkIndex} which was expected to be generated already.");
            return;
        }

        if (value.Socket == null)
        {
            node.AddText($"{name} = {KVGraphNode.StringifyValue(value.Constant!)}");
            return;
        }

        var argInputSocket = node.CreateSocketInFromValueType(name, GetValueTypeFromRegister(chunkIndex, regIndex));
        document.Connect(value.Socket, argInputSocket);
    }

    private void CreateInputsFromRegisterMap(GraphDocument document,
        Node node,
        int chunkIndex,
        Dictionary<int, RegisterValue> registerValues,
        KVObject registerMap)
    {
        var inParams = registerMap["m_Inparams"];
        if (inParams.IsNull)
        {
            return;
        }

        foreach (var (regName, regIdx) in inParams)
        {
            AddNodeRegisterInput(document, node, chunkIndex, registerValues, (int)regIdx, regName);
        }
    }

    // Every potential outflow of a cell, found by shape anywhere in its data
    private List<PulseOutflowConnection> GetCellOutflows(int cellIdx)
    {
        static void GetCellOutflowsRecurse(KVObject obj, List<PulseOutflowConnection> outflowList)
        {
            var outflow = PulseOutflowConnection.FromKV(obj);
            if (outflow is not null)
            {
                outflowList.Add(outflow);
                return;
            }

            foreach (var (_, value) in obj)
            {
                if (value.IsCollection)
                {
                    GetCellOutflowsRecurse(value, outflowList);
                }
                else if (value.IsArray)
                {
                    foreach (var elem in value.AsArraySpan())
                    {
                        if (elem.IsCollection)
                        {
                            GetCellOutflowsRecurse(elem, outflowList);
                        }
                    }
                }
            }
        }

        if (cellOutflows[cellIdx] is not { } outflows)
        {
            outflows = [];
            GetCellOutflowsRecurse(cells[cellIdx], outflows);
            cellOutflows[cellIdx] = outflows;
        }

        return outflows;
    }

    // Instructions that can run right after the given one within the same chunk
    private IEnumerable<int> InstructionSuccessors(IReadOnlyList<KVObject> instructions, int instructionIdx, int chunkIdx)
    {
        var instruction = instructions[instructionIdx];
        switch (GetInstructionType(instruction))
        {
            case InstructionCode.JUMP:
                yield return instruction.GetInt32Property("m_nDestInstruction");
                yield break;
            case InstructionCode.JUMP_COND:
                yield return instruction.GetInt32Property("m_nDestInstruction");
                yield return instructionIdx + 1;
                yield break;
            case InstructionCode.RETURN_VOID or InstructionCode.RETURN_VALUE or InstructionCode.LOOP_BREAK
                or InstructionCode.IMMEDIATE_HALT or InstructionCode.CHUNK_LEAP:
                yield break;
            case InstructionCode.CELL_INVOKE:
                var cellIdx = invokeBindings[instruction.GetInt32Property("m_nInvokeBindingIndex")].GetInt32Property("m_nCellIndex");
                foreach (var outflow in GetCellOutflows(cellIdx))
                {
                    if (outflow.DestChunk == chunkIdx)
                    {
                        yield return outflow.DestInstructionIdx;
                    }
                }
                break;
            case InstructionCode.PULSE_CALL_SYNC or InstructionCode.PULSE_CALL_ASYNC_FIRE or InstructionCode.CREATE_CHILD_CURSOR_OUTFLOW:
                if (instruction.GetInt32Property("m_nChunk") == chunkIdx)
                {
                    yield return instruction.GetInt32Property("m_nDestInstruction");
                }
                break;
        }

        yield return instructionIdx + 1;
    }

    // Finds each loop by its back edge, the jump from the last instruction of the loop back to the first
    private Dictionary<int, int>[] FindLoops()
    {
        var loops = new Dictionary<int, int>[chunks.Count];
        for (var chunkIdx = 0; chunkIdx < chunks.Count; chunkIdx++)
        {
            var instructions = chunks[chunkIdx].GetArray("m_Instructions");
            var chunkLoops = new Dictionary<int, int>();
            var onPath = new bool[instructions.Count];
            var visited = new bool[instructions.Count];

            void Visit(int instructionIdx)
            {
                visited[instructionIdx] = true;
                onPath[instructionIdx] = true;

                foreach (var next in InstructionSuccessors(instructions, instructionIdx, chunkIdx))
                {
                    if (next < 0 || next >= instructions.Count)
                    {
                        continue;
                    }

                    if (onPath[next])
                    {
                        chunkLoops.TryAdd(next, instructionIdx);
                    }
                    else if (!visited[next])
                    {
                        Visit(next);
                    }
                }

                onPath[instructionIdx] = false;
            }

            if (instructions.Count > 0)
            {
                Visit(0);
            }

            loops[chunkIdx] = chunkLoops;
        }

        return loops;
    }

    // A register mentioned at least twice is read somewhere, since one mention is whatever writes it
    private bool IsRegisterUsedInChunk(int chunkIdx, int registerIdx)
    {
        if (chunkIdx < 0 || chunkIdx >= chunks.Count || registerIdx < 0)
        {
            return false;
        }

        if (!registerMentionCounts.TryGetValue(chunkIdx, out var counts))
        {
            counts = CountRegisterMentions(chunkIdx);
            registerMentionCounts[chunkIdx] = counts;
        }

        return counts.GetValueOrDefault(registerIdx) >= 2;
    }

    private Dictionary<int, int> CountRegisterMentions(int chunkIdx)
    {
        Dictionary<int, int> counts = [];

        void Count(int registerIdx) => counts[registerIdx] = counts.GetValueOrDefault(registerIdx) + 1;

        foreach (var instruction in chunks[chunkIdx].GetArray("m_Instructions"))
        {
            Count(instruction.GetInt32Property("m_nReg0"));
            Count(instruction.GetInt32Property("m_nReg1"));
            Count(instruction.GetInt32Property("m_nReg2"));

            var registerMap = GetInstructionType(instruction) switch
            {
                InstructionCode.LIBRARY_INVOKE or InstructionCode.CELL_INVOKE
                    => invokeBindings.ElementAtOrDefault(instruction.GetInt32Property("m_nInvokeBindingIndex"))?["m_RegisterMap"],
                InstructionCode.PULSE_CALL_SYNC or InstructionCode.PULSE_CALL_ASYNC_FIRE
                    => callInfos.ElementAtOrDefault(instruction.GetInt32Property("m_nCallInfoIndex"))?["m_RegisterMap"],
                _ => null,
            };

            if (registerMap is null || registerMap.IsNull)
            {
                continue;
            }

            foreach (var paramsKey in (string[])["m_Inparams", "m_Outparams"])
            {
                var registerParams = registerMap[paramsKey];
                if (registerParams.IsNull)
                {
                    continue;
                }

                foreach (var (_, registerIdx) in registerParams)
                {
                    Count((int)registerIdx);
                }
            }
        }

        return counts;
    }

    // A node whose outputs are never read is drawn as a step in the flow, otherwise it would dangle
    // as a value provider nothing reads.
    private FlowContinuation SetupNodeOutputsFromRegisterMap(
        GraphDocument document,
        Node node,
        int chunkIndex,
        Dictionary<int, RegisterValue> registerValues,
        FlowContinuation previousActionOutSocket,
        KVObject registerMap)
    {
        if (!TryAddRegisterMapOutParams(node, chunkIndex, registerValues, registerMap)
            || !registerMap["m_Outparams"].Any(outParam => IsRegisterUsedInChunk(chunkIndex, (int)outParam.Value)))
        {
            return CreateSequentialActionSockets(document, node, previousActionOutSocket);
        }

        return previousActionOutSocket;
    }

    private Node CreateIfNode(
        GraphDocument document,
        KVObject instruction,
        int chunkIndex,
        FlowContinuation previousActionOutSocket,
        Dictionary<int, RegisterValue> registerValues)
    {
        var node = CreateNode("If", "Flow control", PulseCategory.FlowControl);
        CreateSequentialActionSockets(document, node, previousActionOutSocket);

        var reg0 = instruction.GetInt32Property("m_nReg0");
        if (reg0 != -1)
        {
            AddNodeRegisterInput(document, node, chunkIndex, registerValues, reg0, "Condition");
        }

        return node;
    }

    // Starts a child cursor at the destination while this flow carries on
    private FlowContinuation AddChildCursorNode(
        GraphDocument document,
        string name,
        PulseCategory category,
        int sourceChunk,
        int destChunk,
        int destInstructionIdx,
        FlowContinuation previousActionOutSocket,
        Dictionary<int, RegisterValue> registerValues)
    {
        var node = CreateNode(name, "Flow", category);
        var nextActionOutSocket = CreateSequentialActionSockets(document, node, previousActionOutSocket);

        TraverseOutflow(document, node, "Child cursor", sourceChunk, destChunk, destInstructionIdx, int.MaxValue, registerValues);

        document.AddNode(node);
        return nextActionOutSocket;
    }

    // A leap always starts the target chunk from its first instruction, m_nDestInstruction is not used.
    // Leaping into the current chunk restarts it.
    private FlowContinuation AddChunkLeapNode(GraphDocument document, KVObject instruction, FlowContinuation previousActionOutSocket)
    {
        var node = CreateNode("Chunk Leap", "Flow", PulseCategory.FlowControl);
        var nextActionOutSocket = CreateSequentialActionSockets(document, node, previousActionOutSocket);

        remoteNodesToResolve.Add(new(instruction.GetInt32Property("m_nChunk"), node, "Target: "));

        return nextActionOutSocket;
    }

    // Draws the loop from instructionIdx to its jump back at loopEnd and moves instructionIdx past it.
    // Returns false when the instructions do not match a known loop shape.
    private bool TryTraverseLoop(
        GraphDocument document,
        int chunkIndex,
        int loopEnd,
        ref int instructionIdx,
        ref FlowContinuation previousActionOutSocket,
        Dictionary<int, RegisterValue> registerValues)
    {
        var loopStart = instructionIdx;
        var chunk = chunks[chunkIndex];
        var instructions = chunk.GetArray("m_Instructions");
        var registers = chunk.GetArray("m_Registers");
        var loopEndInstr = instructions[loopEnd];

        if (GetInstructionType(loopEndInstr) == InstructionCode.JUMP_COND)
        {
            var doWhileNode = CreateNode("Do-While Loop", "Flow control", PulseCategory.FlowControl);
            previousActionOutSocket = CreateSequentialActionSockets(document, doWhileNode, previousActionOutSocket);

            var doWhileRegisterValues = new Dictionary<int, RegisterValue>(registerValues);
            previousActionOutSocket = TraverseNodesForChunk(document, chunkIndex, previousActionOutSocket, doWhileRegisterValues, loopStart, loopEnd);

            AddNodeRegisterInput(document, doWhileNode, chunkIndex, doWhileRegisterValues, loopEndInstr.GetInt32Property("m_nReg0"), "Condition");
            document.AddNode(doWhileNode);
            instructionIdx = loopEnd + 1;
            return true;
        }

        var instrJumpCompIdx = -1;
        for (var i = loopStart; i <= loopEnd; i++)
        {
            if (GetInstructionType(instructions[i]) == InstructionCode.JUMP_COND)
            {
                instrJumpCompIdx = i;
                break;
            }
        }

        if (instrJumpCompIdx == -1)
        {
            // Loop can happen with async calls, but it's safe to proceed.
            ProgressReporter?.Report($"Could not find conditional jump instruction for loop starting at instruction {loopStart} to {loopEnd} in chunk {chunkIndex}. Possibly asynchronous loop?");
            return false;
        }

        var loopJumpOutInstructionIdx = instrJumpCompIdx + 1;
        var loopJumpOutInstruction = instructions[loopJumpOutInstructionIdx];

        if (GetInstructionType(loopJumpOutInstruction) != InstructionCode.JUMP)
        {
            ProgressReporter?.Report($"Could not find jump-out instruction for loop starting at instruction {loopStart} in chunk {chunkIndex}");
            instructionIdx = loopEnd + 1;
            return false;
        }

        var outsideLoopTargetInstructionIdx = Math.Max(loopEnd + 1, loopJumpOutInstruction.GetInt32Property("m_nDestInstruction"));

        var condRegister = instructions[instrJumpCompIdx].GetInt32Property("m_nReg0");
        if (instrJumpCompIdx > loopStart)
        {
            // fills out nodes between the loop start and the first jump_cond belonging to it
            previousActionOutSocket = TraverseNodesForChunk(document, chunkIndex, previousActionOutSocket, registerValues, loopStart, instrJumpCompIdx);
        }

        // Debug origin names read "<node id>:<port>", the loop node's own registers share its id
        var conditionRegInfo = registers[condRegister];
        var conditionOriginName = conditionRegInfo.GetStringProperty("m_OriginName");
        var loopNodePrefix = conditionOriginName[..(conditionOriginName.IndexOf(':', StringComparison.Ordinal) + 1)];
        var instrComp = instructions[conditionRegInfo.GetInt32Property("m_nWrittenByInstruction")];

        // assuming a 'for' loop, one register is going to be the index (can find out through originName)
        // the other one will be the max/min value
        var regStart = -1;
        var regStep = -1;
        var regStop = -1;

        foreach (var regIdx in (int[])[instrComp.GetInt32Property("m_nReg1"), instrComp.GetInt32Property("m_nReg2")])
        {
            if (regIdx == -1)
            {
                continue;
            }

            if (registers[regIdx].GetStringProperty("m_OriginName").EndsWith("__loop_index", StringComparison.Ordinal))
            {
                regStart = regIdx;
            }
            else
            {
                regStop = regIdx;
            }
        }

        // if we did not find a register with __loop_index, we can not be sure if the other one is the stop index.
        if (regStart == -1)
        {
            regStop = -1;
        }

        if (regStop == -1 || regStart == -1)
        {
            foreach (var reg in registers)
            {
                var originName = reg.GetStringProperty("m_OriginName");
                if (!originName.StartsWith(loopNodePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (originName.EndsWith("m_Stop", StringComparison.Ordinal))
                {
                    regStop = reg.GetInt32Property("m_nReg");
                }
                else if (originName.EndsWith("m_Step", StringComparison.Ordinal))
                {
                    regStep = reg.GetInt32Property("m_nReg");
                }
                else if (originName.EndsWith("m_Start", StringComparison.Ordinal))
                {
                    regStart = reg.GetInt32Property("m_nReg");
                }
            }
        }

        var forLoopNode = CreateNode("Loop", "Flow control", PulseCategory.FlowControl);

        // No info? One last try, but this is an assumption already.
        // If the latest condition instruction is LT/LTE then in theory we can connect the start and end condition sockets.
        // There are no greater-than instructions, those compile to LT/LTE with swapped operands.
        if (regStop == -1 && regStart == -1)
        {
            if (GetInstructionType(instrComp) is InstructionCode.LT or InstructionCode.LTE)
            {
                regStart = instrComp.GetInt32Property("m_nReg1");
                regStop = instrComp.GetInt32Property("m_nReg2");
            }
            forLoopNode.AddMessage("Loop range may not be accurate (missing debug info)");
        }

        CreateSequentialActionSockets(document, forLoopNode, previousActionOutSocket);

        if (regStart != -1)
        {
            AddNodeRegisterInput(document, forLoopNode, chunkIndex, registerValues, regStart, "First index");
            // add the index output
            // this will be remembered when we do a loop iteration (should also handle foreach type of loop)
            registerValues[regStart] = new(forLoopNode.AddOutput("Index", GraphHue.Amber), null);
        }
        else
        {
            forLoopNode.AddMessage("Could not find start index");
        }

        if (regStop != -1)
        {
            AddNodeRegisterInput(document, forLoopNode, chunkIndex, registerValues, regStop, "Last index");
        }
        else
        {
            forLoopNode.AddMessage("Could not find end index");
        }

        var regIncrementLate = -1;
        var loopOperationEndInstructionIdx = loopEnd;
        if (regStep != -1)
        {
            loopOperationEndInstructionIdx = loopEnd - 1; // The ADD/SUB instruction
            AddNodeRegisterInput(document, forLoopNode, chunkIndex, registerValues, regStep, "Increment");
        }
        else
        {
            var instrLastInLoop = instructions[loopEnd - 1];
            // A bit crude, but otherwise we do not really have a good way of determining the increment
            if (GetInstructionType(instrLastInLoop) is InstructionCode.ADD or InstructionCode.SUB or InstructionCode.MUL or InstructionCode.DIV)
            {
                // use the value that's not the output one, so the increment
                var opReg0 = instrLastInLoop.GetInt32Property("m_nReg0");
                var opReg1 = instrLastInLoop.GetInt32Property("m_nReg1");

                // Traversing the inside of the loop is required first to determine how the increment connects.
                // Increment will be added after traversing.
                regIncrementLate = opReg1 != opReg0 ? opReg1 : instrLastInLoop.GetInt32Property("m_nReg2");
                loopOperationEndInstructionIdx = loopEnd - 1;
            }
        }

        AddNodeRegisterInput(document, forLoopNode, chunkIndex, registerValues, condRegister, "Loop condition");

        if (loopOperationEndInstructionIdx == loopJumpOutInstructionIdx)
        {
            ProgressReporter?.Report($"Potentially empty loop (chunk={chunkIndex}, instruction={loopOperationEndInstructionIdx})");
        }

        var socketOutLoopAction = forLoopNode.CreateFlowOut("Loop");

        var bodyRegisterValues = new Dictionary<int, RegisterValue>(registerValues);
        TraverseNodesForChunk(
            document,
            chunkIndex,
            FlowContinuation.Of(socketOutLoopAction),
            bodyRegisterValues,
            loopJumpOutInstructionIdx + 1,
            loopOperationEndInstructionIdx
        );

        if (regIncrementLate != -1)
        {
            AddNodeRegisterInput(document, forLoopNode, chunkIndex, bodyRegisterValues, regIncrementLate, "Increment");
        }

        previousActionOutSocket = FlowContinuation.Of(forLoopNode.CreateFlowOut("Finished"));

        document.AddNode(forLoopNode);
        // do stuff outside the loop
        instructionIdx = outsideLoopTargetInstructionIdx;
        return true;
    }

    private FlowContinuation TraverseNodesForChunk(GraphDocument document,
        int chunkIndex,
        FlowContinuation sourceActionOutSocket,
        Dictionary<int, RegisterValue> registerValues,
        int startingInstructionIdx = 0,
        int endingInstructionIdx = int.MaxValue /* non-inclusive */)
    {
        if (chunkIndex < 0)
        {
            return sourceActionOutSocket;
        }

        var instructions = chunks[chunkIndex].GetArray("m_Instructions");
        var chunkLoops = loopsByChunk[chunkIndex];

        var finalEndingInstructionIdx = Math.Min(instructions.Count, endingInstructionIdx);
        var previousActionOutSocket = sourceActionOutSocket;
        for (var instructionIdx = startingInstructionIdx; instructionIdx < finalEndingInstructionIdx; instructionIdx++)
        {
            // A loop ending past the range being walked is not the one currently being drawn
            while (chunkLoops.TryGetValue(instructionIdx, out var loopEnd) && finalEndingInstructionIdx > loopEnd)
            {
                if (!TryTraverseLoop(document, chunkIndex, loopEnd, ref instructionIdx, ref previousActionOutSocket, registerValues))
                {
                    break;
                }
            }

            if (instructionIdx >= finalEndingInstructionIdx)
            {
                break;
            }

            var instruction = instructions[instructionIdx];
            var instrType = GetInstructionType(instruction);
            switch (instrType)
            {
                case InstructionCode.LIBRARY_INVOKE:
                {
                    var binding = invokeBindings[instruction.GetInt32Property("m_nInvokeBindingIndex")];
                    var registerMap = binding["m_RegisterMap"];
                    var node = CreateNode(binding.GetStringProperty("m_FuncName"), "Function", PulseCategory.Call);

                    previousActionOutSocket = SetupNodeOutputsFromRegisterMap(document, node, chunkIndex, registerValues, previousActionOutSocket, registerMap);
                    CreateInputsFromRegisterMap(document, node, chunkIndex, registerValues, registerMap);

                    document.AddNode(node);
                    break;
                }
                case InstructionCode.CELL_INVOKE:
                {
                    var binding = invokeBindings[instruction.GetInt32Property("m_nInvokeBindingIndex")];
                    var registerMap = binding["m_RegisterMap"];
                    var funcName = binding.GetStringProperty("m_FuncName");
                    var cellIndex = binding.GetInt32Property("m_nCellIndex");

                    // show name after '::' separator, if can't find then show full name
                    var funcNameSplitIdx = funcName.IndexOf("::", StringComparison.Ordinal);
                    var methodName = funcNameSplitIdx >= 0 ? funcName[(funcNameSplitIdx + 2)..] : funcName;
                    var node = CreateNode(GetCellName(cellIndex), methodName, GetCellCategory(cellIndex));

                    previousActionOutSocket = SetupNodeOutputsFromRegisterMap(document, node, chunkIndex, registerValues, previousActionOutSocket, registerMap);
                    AddFilteredCellDetails(node, cellIndex);
                    CreateInputsFromRegisterMap(document, node, chunkIndex, registerValues, registerMap);
                    PopulateCellAndTraverseOutflows(document, node, cellIndex, chunkIndex, registerValues, finalEndingInstructionIdx);

                    document.AddNode(node);
                    break;
                }
                case InstructionCode.GET_CONST:
                {
                    var constIdx = instruction.GetInt32Property("m_nConstIdx");
                    var constant = constants.ElementAtOrDefault(constIdx);
                    if (constant == null)
                    {
                        ProgressReporter?.Report($"Failed to retrieve constant of ID={constIdx}");
                        break;
                    }

                    registerValues[instruction.GetInt32Property("m_nReg0")] = new(null, constant["m_Value"]);
                    break;
                }
                case InstructionCode.GET_DOMAIN_VALUE:
                {
                    var domainValIdx = instruction.GetInt32Property("m_nDomainValueIdx");
                    var domainValue = domainValues.ElementAtOrDefault(domainValIdx);
                    if (domainValue == null)
                    {
                        ProgressReporter?.Report($"Failed to retrieve domain value of ID={domainValIdx}");
                        break;
                    }

                    registerValues[instruction.GetInt32Property("m_nReg0")] = new(null, domainValue["m_Value"]);
                    break;
                }
                case InstructionCode.GET_VAR:
                case InstructionCode.GET_VAR_DETACH:
                {
                    var hub = VariableHubFromInstruction(document, instruction, out var name);
                    AddVariableRead(document, hub, "Get Variable", name, chunkIndex, instruction.GetInt32Property("m_nReg0"), registerValues);
                    break;
                }
                case InstructionCode.SET_VAR:
                case InstructionCode.SET_VAR_OBSERVABLE:
                {
                    var hub = VariableHubFromInstruction(document, instruction, out var name);
                    previousActionOutSocket = AddVariableWrite(document, hub, "Set Variable", name, chunkIndex, instruction.GetInt32Property("m_nReg0"),
                        previousActionOutSocket, registerValues);
                    break;
                }
                case InstructionCode.SET_VAR_ARRAY_ELEMENT_1D:
                {
                    // Value comes from reg0 and the element index from reg2, reg1 is unused
                    var hub = VariableHubFromInstruction(document, instruction, out var name);
                    previousActionOutSocket = AddVariableWrite(document, hub, "Set Array Element", name, chunkIndex, instruction.GetInt32Property("m_nReg0"),
                        previousActionOutSocket, registerValues, instruction.GetInt32Property("m_nReg2"));
                    break;
                }
                case InstructionCode.GET_TEMPVAR:
                {
                    var hub = TempVariableHubFromInstruction(document, chunkIndex, instruction, out var name);
                    AddVariableRead(document, hub, "Get Temporary Variable", name, chunkIndex, instruction.GetInt32Property("m_nReg0"), registerValues);
                    break;
                }
                case InstructionCode.SET_TEMPVAR:
                case InstructionCode.SET_TEMPVAR_OBSERVABLE:
                {
                    var hub = TempVariableHubFromInstruction(document, chunkIndex, instruction, out var name);
                    previousActionOutSocket = AddVariableWrite(document, hub, "Set Temporary Variable", name, chunkIndex, instruction.GetInt32Property("m_nReg0"),
                        previousActionOutSocket, registerValues);
                    break;
                }
                case InstructionCode.GET_BLACKBOARD_REFERENCE:
                {
                    var hub = BlackboardReferenceHubFromInstruction(document, instruction, out var name);
                    AddVariableRead(document, hub, "Get Blackboard Reference", name, chunkIndex, instruction.GetInt32Property("m_nReg0"), registerValues);
                    break;
                }
                case InstructionCode.SET_BLACKBOARD_REFERENCE:
                {
                    var hub = BlackboardReferenceHubFromInstruction(document, instruction, out var name);
                    previousActionOutSocket = AddVariableWrite(document, hub, "Set Blackboard Reference", name, chunkIndex, instruction.GetInt32Property("m_nReg0"),
                        previousActionOutSocket, registerValues);
                    break;
                }
                case InstructionCode.NOP:
                case InstructionCode.DETACH_REGISTER:
                {
                    // Detaching only changes how the register holds its value, nothing to show
                    break;
                }
                case InstructionCode.CREATE_CHILD_CURSOR_OUTFLOW:
                {
                    previousActionOutSocket = AddChildCursorNode(document, "Start Child Cursor", PulseCategory.FlowControl, chunkIndex,
                        instruction.GetInt32Property("m_nChunk"), instruction.GetInt32Property("m_nDestInstruction"), previousActionOutSocket, registerValues);
                    break;
                }
                case InstructionCode.LOOP_BREAK:
                {
                    // Leaves the nearest enclosing loop, resuming where the call into its body says to
                    var node = CreateNode("Break", "Flow", PulseCategory.FlowControl);
                    CreateSequentialActionSockets(document, node, previousActionOutSocket);
                    node.AddText("Exits enclosing loop");

                    // Without any loop body call, the break reaches a method call boundary or the
                    // bottom of the stack and halts the cursor.
                    if (!hasLoopBodyCalls)
                    {
                        node.AddMessage("No enclosing loop, halts the cursor");
                    }
                    document.AddNode(node);
                    return previousActionOutSocket;
                }
                case InstructionCode.PULSE_CALL_SYNC:
                case InstructionCode.PULSE_CALL_ASYNC_FIRE:
                {
                    var callTargetChunk = instruction.GetInt32Property("m_nChunk");
                    var callDestInstructionIdx = instruction.GetInt32Property("m_nDestInstruction");
                    if (callTargetChunk == chunkIndex && callDestInstructionIdx > 0)
                    {
                        if (instrType == InstructionCode.PULSE_CALL_ASYNC_FIRE)
                        {
                            previousActionOutSocket = AddChildCursorNode(document, "Call Asynchronously", PulseCategory.Call,
                                chunkIndex, chunkIndex, callDestInstructionIdx, previousActionOutSocket, registerValues);
                        }
                        else
                        {
                            // A call within the same chunk runs inline, e.g. a loop body, and comes back to the next instruction
                            previousActionOutSocket = TraverseNodesForChunk(document, chunkIndex, previousActionOutSocket,
                                new Dictionary<int, RegisterValue>(registerValues), callDestInstructionIdx);
                        }
                        break;
                    }

                    var node = CreateNode(instrType == InstructionCode.PULSE_CALL_SYNC ? "Call" : "Call Asynchronously", "Flow", PulseCategory.Call);
                    previousActionOutSocket = CreateSequentialActionSockets(document, node, previousActionOutSocket);

                    var callInfoIndex = instruction.GetInt32Property("m_nCallInfoIndex");
                    var callInfo = callInfos.ElementAtOrDefault(callInfoIndex);
                    if (callInfo != null)
                    {
                        CreateInputsFromRegisterMap(document, node, chunkIndex, registerValues, callInfo["m_RegisterMap"]);
                    }
                    else
                    {
                        ProgressReporter?.Report($"Failed to retrieve call info of ID={callInfoIndex}.");
                    }

                    remoteNodesToResolve.Add(new(callTargetChunk, node, "Method: "));
                    break;
                }
                case InstructionCode.RETURN_VALUE:
                {
                    var node = CreateNode("Return Value", "Flow", PulseCategory.FlowControl);
                    previousActionOutSocket = CreateSequentialActionSockets(document, node, previousActionOutSocket);
                    AddNodeRegisterInput(document, node, chunkIndex, registerValues, instruction.GetInt32Property("m_nReg0"), "value");
                    document.AddNode(node);
                    break;
                }
                case InstructionCode.RETURN_VOID:
                case InstructionCode.IMMEDIATE_HALT:
                {
                    return previousActionOutSocket;
                }
                case InstructionCode.JUMP:
                {
                    TraverseNodesForChunk(
                        document,
                        chunkIndex,
                        previousActionOutSocket,
                        new Dictionary<int, RegisterValue>(registerValues),
                        instruction.GetInt32Property("m_nDestInstruction"),
                        finalEndingInstructionIdx);
                    return previousActionOutSocket;
                }
                case InstructionCode.JUMP_COND:
                {
                    var node = CreateIfNode(document, instruction, chunkIndex, previousActionOutSocket, registerValues);

                    // If false we don't take the jump. So traverse starting from currentinstr + 1
                    var destInstructionIdxFalse = instructionIdx + 1;

                    // Find out the jump out instruction after the True case is finished.
                    // Whether the graph code run through true or false, it will end up at one, unless it's just a return
                    // in which case we don't have to worry about anything
                    var firstInstructionAfterBranches = -1;
                    var falseInstruction = instructions[destInstructionIdxFalse];
                    if (GetInstructionType(falseInstruction) == InstructionCode.JUMP)
                    {
                        var falseJumpTarget = falseInstruction.GetInt32Property("m_nDestInstruction");
                        if (falseJumpTarget > 0 && GetInstructionType(instructions[falseJumpTarget - 1]) == InstructionCode.JUMP)
                        {
                            firstInstructionAfterBranches = instructions[falseJumpTarget - 1].GetInt32Property("m_nDestInstruction");
                        }
                    }

                    var branchEndInstructionIdx = firstInstructionAfterBranches == -1 ? finalEndingInstructionIdx : firstInstructionAfterBranches;
                    TraverseOutflow(document, node, "True", chunkIndex, chunkIndex, instruction.GetInt32Property("m_nDestInstruction"), branchEndInstructionIdx, registerValues);
                    TraverseOutflow(document, node, "False", chunkIndex, chunkIndex, destInstructionIdxFalse, branchEndInstructionIdx, registerValues);

                    // create even if we're returning, cause the socket still could be connected to further actions
                    // if the current flow was a subroutine
                    previousActionOutSocket = FlowContinuation.Of(node.CreateFlowOut("Finished"));
                    document.AddNode(node);

                    if (firstInstructionAfterBranches == -1)
                    {
                        return previousActionOutSocket;
                    }

                    instructionIdx = firstInstructionAfterBranches - 1; // next iteration will +1 this
                    break;
                }
                case InstructionCode.CHUNK_LEAP_COND:
                {
                    var node = CreateIfNode(document, instruction, chunkIndex, previousActionOutSocket, registerValues);
                    AddChunkLeapNode(document, instruction, FlowContinuation.Of(node.CreateFlowOut("True")));

                    // Since leaps don't come back after executing we don't have to worry about defining "bounds" for the conditions, unlike regular jumps.
                    // Also no need for a "Finished" socket because no way for true and false flows to merge back again.
                    previousActionOutSocket = FlowContinuation.Of(node.CreateFlowOut("False"));

                    document.AddNode(node);
                    break;
                }
                case InstructionCode.CHUNK_LEAP:
                {
                    // A leap replaces the current flow and never comes back
                    return AddChunkLeapNode(document, instruction, previousActionOutSocket);
                }
                default:
                {
                    var reg0 = instruction.GetInt32Property("m_nReg0");
                    var reg1 = instruction.GetInt32Property("m_nReg1");
                    var reg2 = instruction.GetInt32Property("m_nReg2");

                    if (reg0 == -1) // nothing to do
                    {
                        break;
                    }

                    var node = CreateNode(instruction.GetStringProperty("m_nCode"), "Instruction", PulseCategory.Instruction);

                    if (reg1 != -1)
                    {
                        AddNodeRegisterInput(document, node, chunkIndex, registerValues, reg1, "arg1");
                    }

                    if (reg2 != -1)
                    {
                        AddNodeRegisterInput(document, node, chunkIndex, registerValues, reg2, "arg2");
                    }

                    if (reg1 == -1 && reg2 == -1)
                    {
                        previousActionOutSocket = CreateSequentialActionSockets(document, node, previousActionOutSocket);
                        AddNodeRegisterInput(document, node, chunkIndex, registerValues, reg0, "arg");
                    }

                    // create output socket for this node, and store it for future connections
                    registerValues[reg0] = new(node.CreateSocketOutFromValueType("retval", GetValueTypeFromRegister(chunkIndex, reg0)), null);
                    document.AddNode(node);
                    break;
                }
            }
        }

        return previousActionOutSocket;
    }

    // Generates outflows and labels for specific cells, this is needed as each one can have very different meaning or behavior.
    // Additionally, handle outflows. Explicitly, or automatically.
    private void PopulateCellAndTraverseOutflows(
        GraphDocument document,
        Node node,
        int cellIdx,
        int chunkIndex,
        Dictionary<int, RegisterValue> registerValues,
        int maxInstructionIdx
    )
    {
        HashSet<string> processedOutflowNames = [];
        var cell = cells[cellIdx];

        switch (cell.GetStringProperty("_class"))
        {
            // here we assume that wait is going to be processed sequentially, not out of order, even though it's theoretically possible.
            case "CPulseCell_Inflow_Wait":
            {
                var wakeResume = cell["m_WakeResume"];
                TraverseOutflow(document, node, "OnFinished", chunkIndex, wakeResume.GetInt32Property("m_nDestChunk"),
                    wakeResume.GetInt32Property("m_nInstruction"), maxInstructionIdx, registerValues);
                processedOutflowNames.Add("m_WakeResume");
                break;
            }
            case "CPulseCell_Step_PublicOutput":
            {
                var publicOutput = publicOutputs.ElementAtOrDefault(cell.GetInt32Property("m_OutputIndex"));
                if (publicOutput == null)
                {
                    break;
                }

                var outputName = publicOutput.GetStringProperty("m_Name", $"<NAME UNKNOWN>");
                var outputDesc = publicOutput.GetStringProperty("m_Description", "");

                node.AddText($"Public Output: {outputName}");
                node.AddText($"Description: {outputDesc}");

                // Entity I/O the graph fires when this output triggers
                foreach (var connection in outputConnections)
                {
                    if (connection.GetStringProperty("m_SourceOutput") != outputName)
                    {
                        continue;
                    }

                    var target = $"{connection.GetStringProperty("m_TargetEntity")}.{connection.GetStringProperty("m_TargetInput")}";
                    var param = connection.GetStringProperty("m_Param", "");
                    node.AddText(string.IsNullOrEmpty(param) ? $"Fires {target}" : $"Fires {target}({param})");
                }
                break;
            }
            case "CPulseCell_Timeline":
            {
                foreach (var timelineEvent in cell.GetArray("m_TimelineEvents"))
                {
                    var eventOutflow = PulseOutflowConnection.FromKV(timelineEvent["m_EventOutflow"]);
                    if (eventOutflow is null || eventOutflow.DestChunk == -1)
                    {
                        continue;
                    }

                    var timeFromPrevious = timelineEvent.GetFloatProperty("m_flTimeFromPrevious");
                    var socketLabel = $"(Time from prev: {timeFromPrevious}s) | {eventOutflow.SourceOutflowName}";
                    AddOutflowSocket(document, node, eventOutflow, socketLabel, chunkIndex, registerValues, maxInstructionIdx);
                    processedOutflowNames.Add(eventOutflow.SourceOutflowName);
                }
                break;
            }
        }

        foreach (var outflow in GetCellOutflows(cellIdx))
        {
            if (!processedOutflowNames.Contains(outflow.SourceOutflowName))
            {
                AddOutflowSocket(document, node, outflow, outflow.SourceOutflowName, chunkIndex, registerValues, maxInstructionIdx);
            }
        }
    }

    /// <summary>Fills <paramref name="document"/> with the graph and lays it out.</summary>
    /// <param name="document">The graph to fill.</param>
    public void Build(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        loopsByChunk = FindLoops();

        Dictionary<int, string> chunkFunctionName = [];
        var currentUnknownNamedFuncNumber = 0;

        // Inflow cells
        for (var cellIdx = 0; cellIdx < cells.Count; cellIdx++)
        {
            var cell = cells[cellIdx];
            var cellCategory = GetCellCategory(cellIdx);
            if (cellCategory != PulseCategory.EntryPoint || !cell.ContainsKey("m_EntryChunk"))
            {
                continue;
            }

            var cellNode = CreateNode(GetCellName(cellIdx), cellCategory.ToString(), cellCategory);
            var entryChunkIdx = cell.GetInt32Property("m_EntryChunk");

            Dictionary<int, RegisterValue> registerValues = [];
            if (cell.TryGetValue("m_RegisterMap", out var registerMap))
            {
                TryAddRegisterMapOutParams(cellNode, entryChunkIdx, registerValues, registerMap);
            }

            TraverseNodesForChunk(document, entryChunkIdx, FlowContinuation.Pending(cellNode), registerValues);
            chunkFunctionName.TryAdd(entryChunkIdx, cell.GetStringProperty("m_MethodName"));

            AddFilteredCellDetails(cellNode, cellIdx);

            document.AddNode(cellNode);
        }

        // Resolve chunks that are not referenced by any cell.
        for (var chunkId = 0; chunkId < chunks.Count; chunkId++)
        {
            if (chunkFunctionName.ContainsKey(chunkId))
            {
                continue;
            }

            var newName = $"Unnamed_{++currentUnknownNamedFuncNumber}";
            var cellNode = CreateNode("Function", "", PulseCategory.EntryPoint);

            chunkFunctionName.Add(chunkId, newName);
            cellNode.AddText(newName);

            TraverseNodesForChunk(document, chunkId, FlowContinuation.Pending(cellNode), []);
            document.AddNode(cellNode);
        }

        // General info as a node
        var graphInfoNode = CreateNode("Graph info", "", PulseCategory.Other);

        // Remap some atomic graph keys to more user friendly names for display
        // If some keys change their name in the future, they still will be displayed, just with the raw key name.
        Dictionary<string, string> prettyNameMap = new()
        {
            { "m_DomainIdentifier", "Domain" },
            { "m_DomainSubType", "Domain sub-type" },
            { "m_ParentMapName", "Parent map name" },
            { "m_ParentXmlName", "Parent XML panel" },
        };

        foreach (var (key, value) in graphDefinition)
        {
            if (value.IsArray || value.IsCollection || value.IsNull)
            {
                continue;
            }

            var keyText = prettyNameMap.GetValueOrDefault(key, key);
            graphInfoNode.AddText($"{keyText}: {value}");
        }
        document.AddNode(graphInfoNode);

        // Variable definitions go on the same hub their reads and writes link to, since there's no
        // specific pane for displaying them.
        for (var varIndex = 0; varIndex < variables.Count; varIndex++)
        {
            var variable = variables[varIndex];
            var node = VariableHubFor(document, VariableHubType, -1, varIndex, variable.GetStringProperty("m_Name"), variable);
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
        }

        // Resolve call nodes to display the target function name
        foreach (var (targetChunk, node, targetNamePrefix) in remoteNodesToResolve)
        {
            var methodNameToCall = chunkFunctionName.GetValueOrDefault(targetChunk, $"<INVALID CHUNK {targetChunk}>");
            node.AddText($"{targetNamePrefix}{methodNameToCall}");
            document.AddNode(node);
        }

        document.LayoutNodesPacked();

        document.Legend.AddRange(PulseHues.Legend());
    }

    #region Nodes
    class Node(KVObject? data) : KVGraphNode(data)
    {
        public GraphSocket CreateFlowIn(string text) => AddInput(text, GraphHue.Neutral);
        public GraphSocket CreateFlowOut(string text) => AddOutput(text, GraphHue.Neutral);
        public GraphSocket CreateSocketInFromValueType(string text, PulseValueType valueType) => AddInput(text, HueOfPval(valueType));
        public GraphSocket CreateSocketOutFromValueType(string text, PulseValueType valueType) => AddOutput(text, HueOfPval(valueType));
    }

    #endregion Nodes
}
