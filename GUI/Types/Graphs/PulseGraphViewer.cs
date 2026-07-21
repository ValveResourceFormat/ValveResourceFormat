using System.Globalization;
using System.Linq;
using System.Text;
using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Graphs;

internal class PulseGraphViewer : GLGraphViewer
{
    private readonly KVObject graphDefinition;

    enum CellCategory
    {
        Unspecified,
        Inflow,
        Outflow,
        Step,
        Value,
    };

    enum CellType
    {
        Unknown,
        Timeline,
        CycleRandom,
        CycleShuffled,
        CycleOrdered,
        Wait,
        PublicOutput
    }

    enum InstructionCode
    {
        INVALID,
        IMMEDIATE_HALT,
        RETURN_VOID,
        RETURN_VALUE,
        NOP,
        JUMP,
        JUMP_COND,
        CHUNK_LEAP,
        CHUNK_LEAP_COND,
        PULSE_CALL_SYNC,
        PULSE_CALL_ASYNC_FIRE,

        GET_CONST,
        GET_DOMAIN_VALUE,
        CELL_INVOKE,
        LIBRARY_INVOKE,
        GET_VAR,
        SET_VAR,

        // More exist, but we don't need any specific code for them.
    }

    // For now these are used for visual coloring purposes
    enum PulseValueType
    {
        PVAL_VOID,
        PVAL_BOOL,
        PVAL_INT,
        PVAL_FLOAT,
        PVAL_STRING
    }

    private readonly HashSet<InstructionCode> flowInstructions =
    [
        InstructionCode.IMMEDIATE_HALT,
        InstructionCode.RETURN_VOID,
        InstructionCode.RETURN_VALUE,
        InstructionCode.NOP,
        InstructionCode.JUMP,
        InstructionCode.JUMP_COND,
        InstructionCode.CHUNK_LEAP,
        InstructionCode.CHUNK_LEAP_COND,
    ];

    private readonly IReadOnlyList<KVObject> cells;
    private readonly IReadOnlyList<KVObject> chunks;
    private readonly IReadOnlyList<KVObject> invokeBindings;
    private readonly IReadOnlyList<KVObject> domainValues;
    private readonly IReadOnlyList<KVObject> constants;
    private readonly IReadOnlyList<KVObject> variables;
    private readonly IReadOnlyList<KVObject> publicOutputs;
    private readonly IReadOnlyList<KVObject> callInfos;
    private readonly Dictionary<int, Dictionary<int, GraphSocket>> instructionInputActionSocketMap = [];
    private readonly List<NodeCallInfo> callNodesToResolve = [];
    private Dictionary<int, HashSet<List<int>>> loopInstructionMap = [];

    struct NodeCallInfo
    {
        public int targetChunk;
        public Node node;
    }

    class PulseOutflowConnection
    {
        public string sourceOutflowName { get; private set; }
        public int destChunk { get; private set; }
        public int destInstructionIdx { get; private set; }
        public KVObject? outflowRegisterMap { get; private set; }

        public PulseOutflowConnection(string sourceOutflowName, int destChunk, int destInstructionIdx, KVObject? outflowRegisterMap)
        {
            this.sourceOutflowName = sourceOutflowName;
            this.destChunk = destChunk;
            this.destInstructionIdx = destInstructionIdx;
            this.outflowRegisterMap = outflowRegisterMap;
        }

        public static explicit operator PulseOutflowConnection?(KVObject obj)
        {
            if (obj.TryGetValue("m_SourceOutflowName", out var sourceOutflowName) &&
               obj.TryGetValue("m_nDestChunk", out var destChunk) &&
               obj.TryGetValue("m_nInstruction", out var destInstructionIdx))
            {
                return new PulseOutflowConnection(
                    sourceOutflowName.ToString(CultureInfo.InvariantCulture),
                    destChunk.ToInt32(CultureInfo.InvariantCulture),
                    destInstructionIdx.ToInt32(CultureInfo.InvariantCulture),
                    obj.TryGetValue("m_OutflowRegisterMap", out var outflowRegisterMap) ? outflowRegisterMap : null
                );
            }

            return null;
        }
    }

    #region Socket types
    private struct Flow;
    private struct ValueNumber;

    private static GraphHue HueOf(Type type) => type == typeof(ValueNumber) ? GraphHue.Amber : GraphHue.Neutral;

    private static GraphHue HueOfPval(PulseValueType valueType) => valueType switch
    {
        PulseValueType.PVAL_INT or PulseValueType.PVAL_FLOAT => GraphHue.Amber,
        PulseValueType.PVAL_STRING => GraphHue.Green,
        PulseValueType.PVAL_BOOL => GraphHue.Orange,
        _ => GraphHue.Slate,
    };
    #endregion Socket types

    public PulseGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, new GraphView())
    {
        graphDefinition = data;

        cells = graphDefinition.GetArray("m_Cells");
        chunks = graphDefinition.GetArray("m_Chunks");
        invokeBindings = graphDefinition.GetArray("m_InvokeBindings");
        domainValues = graphDefinition.GetArray("m_DomainValues");
        constants = graphDefinition.GetArray("m_Constants");
        variables = graphDefinition.GetArray("m_Vars");
        publicOutputs = graphDefinition.GetArray("m_PublicOutputs");
        callInfos = graphDefinition.GetArray("m_CallInfos");

        CreateGraph();
    }

    private bool TryAddRegisterMapOutParams(
        Node node,
        int chunkIndex,
        Dictionary<int, GraphSocket> registerOutputSocketMap,
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
            var outSocket = node.CreateSocketOutFromValueType(paramName, GetValueTypeFromRegister(chunkIndex, regValue));
            registerOutputSocketMap[regValue] = outSocket;
        }

        return true;
    }

    private void AddFilteredCellDetails(Node node, int cellIndex)
    {
        var filteredCell = FilterBaseCellFieldsForDisplay(cellIndex);
        foreach (var kvPair in filteredCell)
        {
            node.AddText($"{kvPair.Key} = {KVGraphNode.StringifyValue(kvPair.Value)}");
        }
    }

    private void TraverseOutflow(
        int destChunk,
        int destInstructionIdx,
        int maxInstructionIdx, // non-inclusive, use when there's a need to limit the range inside a loop
        GraphSocket outputSocket,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, GraphSocket> registerOutputSocketMap)
    {
        if (destChunk == -1)
        {
            return;
        }

        if (destInstructionIdx < 0)
        {
            destInstructionIdx = 0;
        }

        TraverseNodesForChunk(
            destChunk,
            outputSocket,
            new Dictionary<int, KVObject>(registerConstValueMap),
            new Dictionary<int, GraphSocket>(registerOutputSocketMap),
            destInstructionIdx,
            maxInstructionIdx
        );
    }

    private void AddOutflowSocket(
        Node node,
        PulseOutflowConnection outflow,
        string socketLabel,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, GraphSocket> registerOutputSocketMap,
        int maxInstructionIdx
    )
    {
        if (outflow.destChunk == -1 || outflow.destInstructionIdx == -1)
        {
            return;
        }

        if (outflow.outflowRegisterMap is not null)
        {
            TryAddRegisterMapOutParams(node, outflow.destChunk, registerOutputSocketMap, outflow.outflowRegisterMap);
        }
        var outputSocket = node.CreateSocketOut<Flow>(socketLabel);
        TraverseOutflow(outflow.destChunk, outflow.destInstructionIdx, maxInstructionIdx, outputSocket, registerConstValueMap, registerOutputSocketMap);
    }

    private CellCategory GetCellCategory(int cellIdx)
    {
        var cell = cells[cellIdx];
        var className = cell.GetStringProperty("_class");

        const string prefix = "CPulseCell_";
        var typeEndIndex = className.IndexOf('_', prefix.Length);
        if (typeEndIndex == -1)
        {
            typeEndIndex = className.Length;
        }

        var nodeType = className[prefix.Length..typeEndIndex];

        if (Enum.TryParse(nodeType, out CellCategory type))
        {
            return type;
        }

        return CellCategory.Unspecified;
    }

    private static InstructionCode GetInstructionType(KVObject instruction)
    {
        var strInstrType = instruction.GetStringProperty("m_nCode");
        if (Enum.TryParse(strInstrType, out InstructionCode instrType))
        {
            return instrType;
        }

        return InstructionCode.INVALID;
    }

    private PulseValueType GetValueTypeFromRegister(int chunkIdx, int regIdx)
    {
        var chunk = chunks[chunkIdx];
        var regInfo = chunk.GetArray("m_Registers")[regIdx];
        var strRegType = regInfo.GetStringProperty("m_Type");
        if (Enum.TryParse(strRegType, out PulseValueType valueType))
        {
            return valueType;
        }

        return PulseValueType.PVAL_VOID;
    }
    private CellType GetCellType(int cellIdx, out string cellTypeString)
    {
        var cell = cells[cellIdx];
        var className = cell.GetStringProperty("_class");
        var index = className.LastIndexOf('_');
        if (index == -1)
        {
            cellTypeString = "Unknown";
            return CellType.Unknown;
        }

        var name = className[(index + 1)..];
        cellTypeString = name;
        if (Enum.TryParse(name, out CellType cellType))
        {
            return cellType;
        }

        return CellType.Unknown;
    }

    private bool TryGetConstantValueFromId(int constantId, out KVObject value)
    {
        if (constantId >= 0 && constantId < constants.Count)
        {
            var constant = constants[constantId];
            value = constant["m_Value"];
            return true;
        }
        value = [];
        return false;
    }

    private bool TryGetDomainValueFromId(int domainValId, out KVObject value)
    {
        if ((domainValId >= 0 && domainValId < domainValues.Count))
        {
            var domainVal = domainValues[domainValId];
            value = domainVal["m_Value"];
            return true;
        }
        value = [];
        return false;
    }

    private readonly Dictionary<int, Node> variableNodes = [];

    // One hub node per graph variable; SET_VAR instructions wire into it, GET_VAR out of it,
    // so a variable's reads and writes are visible connectivity.
    private Node VariableNodeFor(int varIndex, string name)
    {
        if (!variableNodes.TryGetValue(varIndex, out var node))
        {
            node = new Node(varIndex >= 0 && varIndex < variables.Count ? variables[varIndex] : null)
            {
                Name = name,
                NodeType = "Variable",
                Category = GraphHue.Indigo,
            };
            View.AddNode(node);
            variableNodes[varIndex] = node;
        }

        return node;
    }

    private bool TryGetVariableNameFromId(int variableId, out string value)
    {
        if ((variableId >= 0 && variableId < variables.Count))
        {
            var variable = variables[variableId];
            value = variable.GetStringProperty("m_Name");
            return true;
        }
        value = "";
        return false;
    }

    // Filter out some internal fields, keep only what's derived from the base cell class and useful for display
    private KVObject FilterBaseCellFieldsForDisplay(int cellIndex)
    {
        string[] filterKeys = ["_class", "m_EntryChunk", "m_nEditorNodeID"];
        var cell = cells[cellIndex];
        var filteredCell = new KVObject();

        foreach (var key in cell.Keys)
        {
            if (!filterKeys.Contains(key) && !cell[key].IsCollection && !cell[key].IsArray)
            {
                filteredCell[key] = cell[key];
            }
        }
        return filteredCell;
    }

    private GraphSocket CreateSequentialActionSockets(Node node, GraphSocket previousActionOutSocket, int chunkIdx, int instructionIdx)
    {
        var socketIn = node.CreateSocketIn<Flow>("");
        View.Connect(previousActionOutSocket, socketIn);
        instructionInputActionSocketMap[chunkIdx][instructionIdx] = socketIn;

        var socketOut = node.CreateSocketOut<Flow>("");
        return socketOut;
    }

    private void AddNodeRegisterInput(
        Node node,
        int chunkIndex,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, GraphSocket> registerSocketOutputMap,
        int regIndex,
        string name)
    {
        if (registerSocketOutputMap.TryGetValue(regIndex, out var regOutSocket))
        {
            var argInputSocket = node.CreateSocketInFromValueType(name, GetValueTypeFromRegister(chunkIndex, regIndex));
            View.Connect(regOutSocket, argInputSocket);
            return;
        }
        else if (registerConstValueMap.TryGetValue(regIndex, out var obj))
        {
            node.AddText($"{name} = {KVGraphNode.StringifyValue(obj)}");
            return;
        }
        else
        {
            node.AddText($"{name} = <FAILED TO RESOLVE>");
            Log.Warn(nameof(PulseGraphViewer), $"Failed to find register id={regIndex} at chunk={chunkIndex} which was expected to be generated already.");
        }
    }

    private Dictionary<int, HashSet<List<int>>> FindGraphInstructionCycles()
    {
        Dictionary<int, HashSet<List<int>>> loopInstructionMap = [];
        for (var chunkIdx = 0; chunkIdx < chunks.Count; chunkIdx++)
        {
            var instructions = chunks[chunkIdx].GetArray("m_Instructions");
            HashSet<List<int>> loopInstructionRanges = [];
            FindGraphCyclesRecursive(instructions, 0, new Stack<int>(), loopInstructionRanges, chunkIdx);
            if (loopInstructionRanges.Count > 0)
            {
                loopInstructionMap[chunkIdx] = loopInstructionRanges;
            }
        }
        return loopInstructionMap;
    }

    // Returns pairs of chunk and instruction for all potential outflows of the provided cell.
    private List<PulseOutflowConnection> GetCellOutflows(int cellIdx)
    {
        List<PulseOutflowConnection> outflows = [];
        static void GetCellOutflowsRecurse(KVObject obj, List<PulseOutflowConnection> outflowList)
        {
            var outflow = (PulseOutflowConnection?)obj;
            if (outflow is not null)
            {
                outflowList.Add(outflow);
                return;
            }

            foreach (var (key, value) in obj)
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
        GetCellOutflowsRecurse(cells[cellIdx], outflows);
        return outflows;
    }

    private void FindGraphCyclesRecursive(IReadOnlyList<KVObject> instructions, int instructionStartIdx, Stack<int> instructionStack, HashSet<List<int>> loopInstructionRanges, int chunkIdx)
    {
        var instructionAmountThisBranch = 0;
        for (var instructionIdx = instructionStartIdx; instructionIdx < instructions.Count; instructionIdx++)
        {
            foreach (var val in instructionStack)
            {
                if (val == instructionIdx)
                {
                    var loopRange = new List<int>();
                    foreach (var stackInstructionIdx in instructionStack)
                    {
                        loopRange.Add(stackInstructionIdx);

                        if (stackInstructionIdx == instructionIdx)
                        {
                            break;
                        }
                    }
                    loopRange.Reverse();
                    loopInstructionRanges.Add(loopRange);
                    for (var popIdx = 0; popIdx < instructionAmountThisBranch; popIdx++)
                    {
                        instructionStack.Pop();
                    }
                    return;
                }
            }
            instructionStack.Push(instructionIdx);
            instructionAmountThisBranch++;
            var instruction = instructions[instructionIdx];
            var instrType = GetInstructionType(instruction);

            if (instrType == InstructionCode.CELL_INVOKE)
            {
                var invokeBindingId = instruction.GetInt32Property("m_nInvokeBindingIndex");
                var invokeBinding = invokeBindings[invokeBindingId];
                var cellIdx = invokeBinding.GetInt32Property("m_nCellIndex");

                var outflows = GetCellOutflows(cellIdx);
                foreach (var outflow in outflows)
                {
                    if (outflow.destChunk != chunkIdx)
                    {
                        continue;
                    }
                    FindGraphCyclesRecursive(instructions, outflow.destInstructionIdx, instructionStack, loopInstructionRanges, chunkIdx);
                }
            }

            if (instrType == InstructionCode.JUMP_COND)
            {
                FindGraphCyclesRecursive(instructions, instruction.GetInt32Property("m_nDestInstruction"), instructionStack, loopInstructionRanges, chunkIdx);
                FindGraphCyclesRecursive(instructions, instructionIdx + 1, instructionStack, loopInstructionRanges, chunkIdx);
                break;
            }
            else if (instrType == InstructionCode.JUMP)
            {
                FindGraphCyclesRecursive(instructions, instruction.GetInt32Property("m_nDestInstruction"), instructionStack, loopInstructionRanges, chunkIdx);
                break;
            }
            else if (instrType == InstructionCode.PULSE_CALL_SYNC)
            {
                if (instruction.GetInt32Property("m_nChunk") == chunkIdx)
                {
                    FindGraphCyclesRecursive(instructions, instruction.GetInt32Property("m_nDestInstruction"), instructionStack, loopInstructionRanges, chunkIdx);
                }
            }
            else if (instrType == InstructionCode.RETURN_VOID || instrType == InstructionCode.RETURN_VALUE)
            {
                break;
            }
        }

        for (var i = 0; i < instructionAmountThisBranch; i++)
        {
            instructionStack.Pop();
        }
    }

    // Detect if the register appears at least twice (one is to be expected for output)
    // This helps detecting whether some library/cell bindings should be connected as action or as a value provider.
    // If the output is not used anywhere, the node visual will be confusing.
    private bool IsRegisterUsedInChunk(int chunkIdx, int registerIdx)
    {
        if (chunkIdx < 0 || chunkIdx >= chunks.Count || registerIdx < 0)
        {
            return false;
        }

        var instructions = chunks[chunkIdx].GetArray("m_Instructions");
        var usageCount = 0;

        void CountRegisterInMap(KVObject registerMap)
        {
            var inParams = registerMap["m_Inparams"];
            if (!inParams.IsNull)
            {
                foreach (var kvPair in inParams)
                {
                    if ((int)kvPair.Value == registerIdx)
                    {
                        usageCount++;
                    }
                }
            }

            var outParams = registerMap["m_Outparams"];
            if (!outParams.IsNull)
            {
                foreach (var kvPair in outParams)
                {
                    if ((int)kvPair.Value == registerIdx)
                    {
                        usageCount++;
                    }
                }
            }
        }

        foreach (var instruction in instructions)
        {
            var reg0 = instruction.GetInt32Property("m_nReg0");
            var reg1 = instruction.GetInt32Property("m_nReg1");
            var reg2 = instruction.GetInt32Property("m_nReg2");

            if (reg0 == registerIdx)
            {
                usageCount++;
            }

            if (reg1 == registerIdx)
            {
                usageCount++;
            }

            if (reg2 == registerIdx)
            {
                usageCount++;
            }

            var instrType = GetInstructionType(instruction);
            if (instrType == InstructionCode.LIBRARY_INVOKE || instrType == InstructionCode.CELL_INVOKE)
            {
                var invokeBindingIndex = instruction.GetInt32Property("m_nInvokeBindingIndex");
                if (invokeBindingIndex >= 0 && invokeBindingIndex < invokeBindings.Count)
                {
                    var registerMap = invokeBindings[invokeBindingIndex]["m_RegisterMap"];
                    if (!registerMap.IsNull)
                    {
                        CountRegisterInMap(registerMap);
                    }
                }
            }
            else if (instrType == InstructionCode.PULSE_CALL_SYNC || instrType == InstructionCode.PULSE_CALL_ASYNC_FIRE)
            {
                var callInfoIndex = instruction.GetInt32Property("m_nCallInfoIndex");
                if (callInfoIndex >= 0 && callInfoIndex < callInfos.Count)
                {
                    var registerMap = callInfos[callInfoIndex]["m_RegisterMap"];
                    if (!registerMap.IsNull)
                    {
                        CountRegisterInMap(registerMap);
                    }
                }
            }

            if (usageCount >= 2)
            {
                return true;
            }
        }

        return false;
    }
    private GraphSocket? SetupNodeOutputsFromRegisterMap(
        Node node,
        int chunkIndex,
        int instructionIdx,
        Dictionary<int, GraphSocket> registerOutputSocketMap,
        GraphSocket previousActionOutSocket,
        KVObject registerMap)
    {
        // If node has no outputs
        if (!TryAddRegisterMapOutParams(node, chunkIndex, registerOutputSocketMap, registerMap))
        {
            return CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
        }
        else
        {
            // Some nodes have outputs but they might not be used anywhere, connect node as sequential action if so.
            var outParams = registerMap["m_Outparams"];
            var hasNoUsedOutputs = true;
            foreach (var (paramName, regIdx) in outParams)
            {
                if (IsRegisterUsedInChunk(chunkIndex, regIdx.ToInt32(CultureInfo.InvariantCulture)))
                {
                    hasNoUsedOutputs = false;
                    break;
                }
            }
            if (hasNoUsedOutputs)
            {
                return CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
            }
        }

        return null;
    }

    private void CreateInputsFromRegisterMap(
        Node node,
        int chunkIndex,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, GraphSocket> registerSocketOutputMap,
        KVObject registerMap)
    {
        var inParams = registerMap["m_Inparams"];
        if (inParams.IsNull)
        {
            return;
        }

        foreach (var kvPair in inParams)
        {
            var regName = kvPair.Key;
            var regIdx = (int)kvPair.Value;

            if (registerConstValueMap.TryGetValue(regIdx, out var regValue))
            {
                node.AddText($"{regName} = {KVGraphNode.StringifyValue(regValue)}");
            }
            else if (registerSocketOutputMap.TryGetValue(regIdx, out var regOutSocket))
            {
                var argInputSocket = node.CreateSocketInFromValueType(regName, GetValueTypeFromRegister(chunkIndex, regIdx));
                View.Connect(regOutSocket, argInputSocket);
            }
            else
            {
                node.AddText($"{regName} = <FAILED TO RESOLVE>");
                Log.Warn(nameof(PulseGraphViewer), $"Failed to find register id={regIdx} at chunk={chunkIndex} which was expected to be generated already.");
            }
        }
    }

    private GraphSocket? TraverseNodesForChunk(
        int chunkIndex,
        GraphSocket sourceActionOutSocket,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, GraphSocket> registerOutputSocketMap,
        int startingInstructionIdx = 0,
        int endingInstructionIdx = int.MaxValue /* non-inclusive */)
    {
        if (chunkIndex < 0)
        {
            return null;
        }

        instructionInputActionSocketMap.TryAdd(chunkIndex, []);

        var chunk = chunks[chunkIndex];
        var instructions = chunk.GetArray("m_Instructions");
        var registers = chunk.GetArray("m_Registers");

        var instructionStartIdx = startingInstructionIdx;
        while (instructionStartIdx < instructions.Count && GetInstructionType(instructions[instructionStartIdx]) == InstructionCode.NOP)
        {
            instructionStartIdx++;
        }

        var finalEndingInstructionIdx = Math.Min(instructions.Count, endingInstructionIdx);
        var previousActionOutSocket = sourceActionOutSocket;
        var stopProcessing = false;
        for (var instructionIdx = startingInstructionIdx; instructionIdx < finalEndingInstructionIdx; instructionIdx++)
        {
            var instruction = instructions[instructionIdx];
            if (stopProcessing)
            {
                break;
            }

            foreach (var loopInstrList in loopInstructionMap.GetValueOrDefault(chunkIndex, []))
            {
                var loopStart = loopInstrList.First();
                var loopEnd = loopInstrList.Last();
                if (instructionIdx == loopStart && finalEndingInstructionIdx > loopEnd) // if we're not already figuring out this loop
                {
                    var loopEndInstr = instructions[loopEnd];
                    // do-while loop
                    if (GetInstructionType(loopEndInstr) == InstructionCode.JUMP_COND)
                    {
                        var doWhileNode = new Node(null)
                        {
                            Name = "Do-While Loop",
                            NodeType = "Flow control",
                        };

                        previousActionOutSocket = CreateSequentialActionSockets(doWhileNode, previousActionOutSocket, chunkIndex, instructionIdx);

                        var newRegisterConstValueMap = new Dictionary<int, KVObject>(registerConstValueMap);
                        var newRegisterOutputSocketMap = new Dictionary<int, GraphSocket>(registerOutputSocketMap);
                        var outSocket = TraverseNodesForChunk(
                            chunkIndex,
                            previousActionOutSocket,
                            newRegisterConstValueMap,
                            newRegisterOutputSocketMap,
                            loopStart,
                            loopEnd
                        );

                        if (outSocket != null)
                        {
                            previousActionOutSocket = outSocket;
                        }

                        var condRegister = loopEndInstr.GetInt32Property("m_nReg0");
                        AddNodeRegisterInput(doWhileNode, chunkIndex, newRegisterConstValueMap, newRegisterOutputSocketMap, condRegister, "Condition");
                        View.AddNode(doWhileNode);
                        instructionIdx = loopEnd + 1;
                    }
                    else
                    {
                        var instrJumpCompIdx = -1;
                        KVObject? instrJumpComp = null;
                        foreach (var instructionIdxInstrList in loopInstrList)
                        {
                            var currInstruction = instructions[instructionIdxInstrList];
                            var currInstrType = GetInstructionType(currInstruction);

                            if (currInstrType == InstructionCode.JUMP_COND)
                            {
                                instrJumpCompIdx = instructionIdxInstrList;
                                instrJumpComp = currInstruction;
                                break;
                            }
                        }

                        if (instrJumpComp == null)
                        {
                            // Loop can happen with async calls, but it's safe to proceed.
                            Log.Warn(nameof(PulseGraphViewer), $"Could not find conditional jump instruction for loop starting at instruction {loopStart} to {loopEnd} in chunk {chunkIndex}. Possibly asynchronous loop?");
                            break;
                        }

                        var loopJumpOutInstructionIdx = instrJumpCompIdx + 1;
                        var loopJumpOutInstruction = instructions[loopJumpOutInstructionIdx];

                        if (GetInstructionType(loopJumpOutInstruction) != InstructionCode.JUMP)
                        {
                            Log.Warn(nameof(PulseGraphViewer), $"Could not find jump-out instruction for loop starting at instruction {loopStart} in chunk {chunkIndex}");
                            instructionIdx = loopEnd + 1;
                            break;
                        }

                        var outsideLoopTargetInstructionIdx = Math.Max(loopEnd + 1, loopJumpOutInstruction.GetInt32Property("m_nDestInstruction"));

                        var condRegister = instrJumpComp.GetInt32Property("m_nReg0");
                        if (instrJumpCompIdx > loopStart)
                        {
                            // fills out nodes between the loop start and the first jump_cond belonging to it
                            var outAction = TraverseNodesForChunk(
                                chunkIndex,
                                previousActionOutSocket,
                                registerConstValueMap,
                                registerOutputSocketMap,
                                loopStart,
                                instrJumpCompIdx
                            );
                            if (outAction != null)
                            {
                                previousActionOutSocket = outAction;
                            }
                        }

                        var conditionRegInfo = registers[instrJumpComp.GetInt32Property("m_nReg0")];
                        var originName = conditionRegInfo.GetStringProperty("m_OriginName");

                        var relevantNodeNumber = originName.Split(':')[0];
                        var nodeId = GetInstructionFlowId(chunkIndex, instructionIdx);
                        var instrComp = instructions[conditionRegInfo.GetInt32Property("m_nWrittenByInstruction")];

                        // assuming a 'for' loop, one register is going to be the index (can find out through originName)
                        // the other one will be the max/min value
                        // Also they will have the same node-id specified in OriginName that we can also check to verify
                        IReadOnlyList<int> regs = [instrComp.GetInt32Property("m_nReg1"), instrComp.GetInt32Property("m_nReg2")];
                        var regStart = -1;
                        var regStep = -1;
                        var regStop = -1;
                        var isIncrementLoadedInitially = false;

                        foreach (var regIdx in regs)
                        {
                            if (regIdx == -1)
                            {
                                continue;
                            }

                            var regData = registers[regIdx];
                            var originNameLoop = regData.GetStringProperty("m_OriginName");
                            if (originNameLoop.EndsWith("__loop_index", StringComparison.InvariantCulture))
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
                                var originNameCurr = reg.GetStringProperty("m_OriginName");
                                if (!originNameCurr.StartsWith(relevantNodeNumber, StringComparison.InvariantCulture))
                                {
                                    continue;
                                }

                                if (originNameCurr.EndsWith("m_Stop", StringComparison.InvariantCulture))
                                {
                                    regStop = reg.GetInt32Property("m_nReg");
                                }
                                else if (originNameCurr.EndsWith("m_Step", StringComparison.InvariantCulture))
                                {
                                    regStep = reg.GetInt32Property("m_nReg");
                                    isIncrementLoadedInitially = true;
                                }
                                else if (originNameCurr.EndsWith("m_Start", StringComparison.InvariantCulture))
                                {
                                    regStart = reg.GetInt32Property("m_nReg");
                                }
                            }
                        }

                        // If we have debug info, then just iterate all registers and find matching labelled increment for the flow id
                        if (regStep == -1 && nodeId != -1)
                        {
                            for (var regIdx = 0; regIdx < registers.Count; regIdx++)
                            {
                                var regData = registers[regIdx];
                                var originNameIncrement = regData.GetStringProperty("m_OriginName");
                                if (!originNameIncrement.StartsWith(nodeId.ToString(CultureInfo.InvariantCulture), StringComparison.InvariantCultureIgnoreCase))
                                {
                                    continue;
                                }

                                if (regData.GetStringProperty("m_OriginName").EndsWith("__increment", StringComparison.InvariantCulture))
                                {
                                    regStep = regIdx;
                                    break;
                                }
                            }
                        }

                        var forLoopNode = new Node(null)
                        {
                            Name = "Loop",
                            NodeType = "Flow control",
                        };

                        // No info? One last try, but this is an assumption already.
                        // If the latest condition instruction is LT*/LTE* or GT*/GTE* then in theory we can connect the start and end condition sockets
                        if (regStop == -1 && regStart == -1)
                        {
                            var instrCompName = instrComp.GetStringProperty("m_nCode");
                            if (instrCompName.StartsWith("LT", StringComparison.InvariantCultureIgnoreCase))
                            {
                                regStart = instrComp.GetInt32Property("m_nReg1");
                                regStop = instrComp.GetInt32Property("m_nReg2");
                            }
                            else if (instrCompName.StartsWith("GT", StringComparison.InvariantCultureIgnoreCase))
                            {
                                forLoopNode.AddMessage("Iteration may go higher to lower value");
                                regStart = instrComp.GetInt32Property("m_nReg1");
                                regStop = instrComp.GetInt32Property("m_nReg2");
                            }
                            forLoopNode.AddMessage("Loop range may not be accurate (missing debug info)");
                        }

                        var loopSocketIn = forLoopNode.CreateSocketIn<Flow>("");
                        View.Connect(previousActionOutSocket, loopSocketIn);
                        instructionInputActionSocketMap[chunkIndex][instructionIdx] = loopSocketIn;

                        if (regStart != -1)
                        {
                            AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, regStart, "First index");
                            // add the index output
                            // this will be remembered when we do a loop iteration (should also handle foreach type of loop)
                            registerOutputSocketMap[regStart] = forLoopNode.CreateSocketOut<ValueNumber>("Index");
                        }
                        else
                        {
                            forLoopNode.AddMessage("Could not find start index");
                        }

                        if (regStop != -1)
                        {
                            AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, regStop, "Last index");
                        }
                        else
                        {
                            forLoopNode.AddMessage("Could not find end index");
                        }

                        var regIncrementLate = -1;
                        var loopOperationEndInstructionIdx = loopEnd;
                        if (!isIncrementLoadedInitially)
                        {
                            var instrLastInLoop = instructions[loopEnd - 1];
                            var instrNameStr = instrLastInLoop.GetStringProperty("m_nCode");

                            // A bit crude, but otherwise we do not really have a good way of determining the increment
                            if (instrNameStr.StartsWith("ADD", StringComparison.InvariantCultureIgnoreCase)
                                || instrNameStr.StartsWith("SUB", StringComparison.InvariantCultureIgnoreCase)
                                || instrNameStr.StartsWith("MUL", StringComparison.InvariantCultureIgnoreCase)
                                || instrNameStr.StartsWith("DIV", StringComparison.InvariantCultureIgnoreCase))
                            {
                                var opReg0 = instrLastInLoop.GetInt32Property("m_nReg0");
                                var opReg1 = instrLastInLoop.GetInt32Property("m_nReg1");
                                var opReg2 = instrLastInLoop.GetInt32Property("m_nReg2");

                                // use the value that's not the output one, so the increment
                                var incrementReg = opReg2;
                                if (opReg1 != opReg0)
                                {
                                    incrementReg = opReg1;
                                }

                                // Traversing the inside of the loop is required first to determine how the increment connects.
                                // Increment will be added after traversing.
                                regIncrementLate = incrementReg;
                                loopOperationEndInstructionIdx = loopEnd - 1;
                            }
                        }
                        else if (regStep != -1)
                        {
                            loopOperationEndInstructionIdx = loopEnd - 1; // The ADD/SUB instruction
                            AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, regStep, "Increment");
                        }
                        else
                        {
                            loopOperationEndInstructionIdx = loopEnd; // No increment?
                            forLoopNode.AddMessage("Could not find the increment");
                        }

                        AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, condRegister, "Loop condition");

                        if (loopOperationEndInstructionIdx == loopJumpOutInstructionIdx)
                        {
                            Log.Info(nameof(PulseGraphViewer), $"Potentially empty loop (chunk={chunkIndex}, instruction={loopOperationEndInstructionIdx})");
                        }

                        var socketOutLoopAction = forLoopNode.CreateSocketOut<Flow>("Loop");

                        var newRegisterConstValueMap = new Dictionary<int, KVObject>(registerConstValueMap);
                        var newRegisterOutputSocketMap = new Dictionary<int, GraphSocket>(registerOutputSocketMap);
                        TraverseNodesForChunk(
                            chunkIndex,
                            socketOutLoopAction,
                            newRegisterConstValueMap,
                            newRegisterOutputSocketMap,
                            loopJumpOutInstructionIdx + 1,
                            loopOperationEndInstructionIdx
                        );

                        if (regIncrementLate != -1)
                        {
                            AddNodeRegisterInput(forLoopNode, chunkIndex, newRegisterConstValueMap, newRegisterOutputSocketMap, regIncrementLate, "Increment");
                        }

                        previousActionOutSocket = forLoopNode.CreateSocketOut<Flow>("Finished");

                        View.AddNode(forLoopNode);
                        // do stuff outside the loop
                        instructionIdx = outsideLoopTargetInstructionIdx;
                    }
                }
            }
            if (instructionIdx >= finalEndingInstructionIdx)
            {
                break;
            }
            // update instruction if changed
            instruction = instructions[instructionIdx];

            var instrType = GetInstructionType(instruction);
            var instrNameString = instruction.GetStringProperty("m_nCode");
            switch (instrType)
            {
                case InstructionCode.LIBRARY_INVOKE:
                    {
                        var invokeIndex = instruction.GetInt32Property("m_nInvokeBindingIndex");
                        var binding = invokeBindings[invokeIndex];
                        var registerMap = binding["m_RegisterMap"];

                        var funcName = binding.GetStringProperty("m_FuncName");
                        var node = new Node(null)
                        {
                            Name = funcName,
                            NodeType = "Function",
                        };

                        var newActionOutSocket = SetupNodeOutputsFromRegisterMap(node, chunkIndex, instructionIdx, registerOutputSocketMap, previousActionOutSocket, registerMap);
                        if (newActionOutSocket != null)
                        {
                            previousActionOutSocket = newActionOutSocket;
                        }


                        CreateInputsFromRegisterMap(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, registerMap);

                        View.AddNode(node);
                        break;
                    }
                case InstructionCode.CELL_INVOKE:
                    {
                        var invokeIndex = instruction.GetInt32Property("m_nInvokeBindingIndex");
                        var binding = invokeBindings[invokeIndex];
                        var registerMap = binding["m_RegisterMap"];

                        var funcName = binding.GetStringProperty("m_FuncName");
                        var cellIndex = binding.GetInt32Property("m_nCellIndex");
                        GetCellType(cellIndex, out var cellName);

                        var funcNameSplitIdx = funcName.IndexOf("::", StringComparison.InvariantCulture);
                        var node = new Node(null)
                        {
                            Name = cellName,
                            // show name after '::' separator, if can't find then show full name
                            NodeType = funcName[(funcNameSplitIdx >= 0 ? (funcNameSplitIdx + 2) : 0)..],
                        };

                        var newActionOutSocket = SetupNodeOutputsFromRegisterMap(node, chunkIndex, instructionIdx, registerOutputSocketMap, previousActionOutSocket, registerMap);
                        if (newActionOutSocket != null)
                        {
                            previousActionOutSocket = newActionOutSocket;
                        }


                        AddFilteredCellDetails(node, cellIndex);
                        CreateInputsFromRegisterMap(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, registerMap);
                        PopulateCellAndTraverseOutflows(node, cellIndex, registerConstValueMap, registerOutputSocketMap, finalEndingInstructionIdx);

                        View.AddNode(node);
                        break;
                    }
                case InstructionCode.GET_CONST:
                    {
                        var constIdx = instruction.GetInt32Property("m_nConstIdx");
                        var outputRegIdx = instruction.GetInt32Property("m_nReg0");
                        if (TryGetConstantValueFromId(constIdx, out var value))
                        {
                            registerConstValueMap[outputRegIdx] = value;
                        }
                        else
                        {
                            Log.Warn(nameof(PulseGraphViewer), $"Failed to retrieve constant of ID={constIdx}");
                        }
                        break;
                    }
                case InstructionCode.GET_DOMAIN_VALUE:
                    {
                        var domainValIdx = instruction.GetInt32Property("m_nDomainValueIdx");
                        var outputRegIdx = instruction.GetInt32Property("m_nReg0");
                        if (TryGetDomainValueFromId(domainValIdx, out var value))
                        {
                            registerConstValueMap[outputRegIdx] = value;
                        }
                        else
                        {
                            Log.Warn(nameof(PulseGraphViewer), $"Failed to retrieve domain value of ID={domainValIdx}");
                        }
                        break;
                    }
                case InstructionCode.GET_VAR:
                    {
                        var varIndex = instruction.GetInt32Property("m_nVar");
                        var regIndex = instruction.GetInt32Property("m_nReg0");
                        var node = new Node(null)
                        {
                            Name = "Get Variable",
                            NodeType = "Instruction",
                        };
                        if (!TryGetVariableNameFromId(varIndex, out var name))
                        {
                            name = $"<UNKNOWN m_nVar={varIndex}>";
                            Log.Warn(nameof(PulseGraphViewer), $"Failed to retrieve variable name of ID={varIndex}. Invalid graph definition?");
                        }

                        node.AddText(name);
                        var outSocket = node.CreateSocketOutFromValueType("retval", GetValueTypeFromRegister(chunkIndex, regIndex));
                        registerOutputSocketMap[regIndex] = outSocket;

                        View.AddNode(node);

                        var variableHub = VariableNodeFor(varIndex, name);
                        var readsOutput = variableHub.Outputs.Find(static o => o.Name == "reads") ?? variableHub.AddOutput("reads", GraphHue.Indigo);
                        View.Connect(readsOutput, node.AddInput("var", GraphHue.Indigo, allowMultiple: true), dashed: true);
                        break;
                    }
                case InstructionCode.SET_VAR:
                    {
                        var varIndex = instruction.GetInt32Property("m_nVar");
                        var regIndex = instruction.GetInt32Property("m_nReg0");
                        var node = new Node(null)
                        {
                            Name = "Set Variable",
                            NodeType = "Instruction",
                        };
                        previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);

                        if (!TryGetVariableNameFromId(varIndex, out var name))
                        {
                            name = $"<UNKNOWN m_nVar={varIndex}>";
                            Log.Warn(nameof(PulseGraphViewer), $"Failed to retrieve variable name of ID={varIndex}. Invalid graph definition?");
                        }

                        node.AddText(name);
                        AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, regIndex, "value");

                        View.AddNode(node);

                        var variableHub = VariableNodeFor(varIndex, name);
                        var writesInput = variableHub.Inputs.Find(static i => i.Name == "writes") ?? variableHub.AddInput("writes", GraphHue.Indigo, allowMultiple: true);
                        View.Connect(node.AddOutput("var", GraphHue.Indigo), writesInput, dashed: true);
                        break;
                    }
                case InstructionCode.PULSE_CALL_SYNC:
                case InstructionCode.PULSE_CALL_ASYNC_FIRE:
                    {
                        var callTargetChunk = instruction.GetInt32Property("m_nChunk");
                        var callDestInstructionIdx = instruction.GetInt32Property("m_nDestInstruction");
                        if (callTargetChunk != chunkIndex || callDestInstructionIdx <= 0)
                        {
                            var callInfoIndex = instruction.GetInt32Property("m_nCallInfoIndex");
                            var node = new Node(null)
                            {
                                Name = instrType == InstructionCode.PULSE_CALL_SYNC ? "Call" : "Call Asynchronously",
                                NodeType = "Flow",
                            };
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            var callInfo = callInfos.ElementAtOrDefault(callInfoIndex);
                            if (callInfo != null)
                            {
                                CreateInputsFromRegisterMap(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, callInfo["m_RegisterMap"]);
                            }
                            else
                            {
                                Log.Warn(nameof(PulseGraphViewer), $"Failed to retrieve call info of ID={callInfoIndex}.");
                            }
                            callNodesToResolve.Add(new NodeCallInfo
                            {
                                targetChunk = callTargetChunk,
                                node = node,
                            });
                        }
                        else
                        {
                            // If within the same chunk then treat that as a jump, don't know what it actually could represent yet besides just that.
                            // The difference here is mostly that we still come back to process the instruction after the call finishes
                            var outSocket = TraverseNodesForChunk(
                                chunkIndex,
                                previousActionOutSocket,
                                new Dictionary<int, KVObject>(registerConstValueMap),
                                new Dictionary<int, GraphSocket>(registerOutputSocketMap),
                                callDestInstructionIdx
                            );

                            if (outSocket != null)
                            {
                                previousActionOutSocket = outSocket;
                            }
                        }
                        break;
                    }
                case InstructionCode.RETURN_VALUE:
                    {
                        var regIndex = instruction.GetInt32Property("m_nReg0");
                        var node = new Node(null)
                        {
                            Name = "Return Value",
                            NodeType = "Flow",
                        };
                        previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                        AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, regIndex, "value");
                        View.AddNode(node);
                        break;
                    }
                case InstructionCode.RETURN_VOID:
                case InstructionCode.IMMEDIATE_HALT:
                    {
                        stopProcessing = true;
                        break;
                    }
                case InstructionCode.JUMP:
                    {
                        stopProcessing = true;
                        var destInstructionIdx = instruction.GetInt32Property("m_nDestInstruction");
                        TraverseNodesForChunk(
                            chunkIndex,
                            previousActionOutSocket,
                            new Dictionary<int, KVObject>(registerConstValueMap),
                            new Dictionary<int, GraphSocket>(registerOutputSocketMap),
                            destInstructionIdx,
                            finalEndingInstructionIdx);
                        break;
                    }
                case InstructionCode.JUMP_COND:
                    {
                        var reg0 = instruction.GetInt32Property("m_nReg0");
                        var node = new Node(null)
                        {
                            Name = "If",
                            NodeType = "Flow control",
                        };
                        var socketIn = node.CreateSocketIn<Flow>("");
                        View.Connect(previousActionOutSocket, socketIn);
                        instructionInputActionSocketMap[chunkIndex][instructionIdx] = socketIn;

                        if (reg0 != -1)
                        {
                            AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, reg0, "Condition");
                        }

                        // If false we don't take the jump. So traverse starting from currentinstr + 1
                        var destInstructionIdxFalse = instructionIdx + 1;

                        // Find out the jump out instruction after the True case is finished.
                        // Whether the graph code run through true or false, it will end up at one, unless it's just a return
                        // in which case we don't have to worry about anything
                        var firstInsturctionAfterBranches = -1;
                        if (GetInstructionType(instructions[destInstructionIdxFalse]) == InstructionCode.JUMP)
                        {
                            var falseJumpTarget = instructions[destInstructionIdxFalse].GetInt32Property("m_nDestInstruction");

                            if (falseJumpTarget > 0)
                            {
                                var instrTypeBefore = GetInstructionType(instructions[falseJumpTarget - 1]);
                                if (instrTypeBefore == InstructionCode.JUMP)
                                {
                                    firstInsturctionAfterBranches = instructions[falseJumpTarget - 1].GetInt32Property("m_nDestInstruction");
                                }
                            }
                        }

                        var socketOutTrue = node.CreateSocketOut<Flow>("True");
                        var destInstructionIdxTrue = instruction.GetInt32Property("m_nDestInstruction");
                        TraverseNodesForChunk(
                            chunkIndex,
                            socketOutTrue,
                            new Dictionary<int, KVObject>(registerConstValueMap),
                            new Dictionary<int, GraphSocket>(registerOutputSocketMap),
                            destInstructionIdxTrue,
                            firstInsturctionAfterBranches == -1 ? finalEndingInstructionIdx : firstInsturctionAfterBranches
                        );

                        var socketOutFalse = node.CreateSocketOut<Flow>("False");
                        TraverseNodesForChunk(
                            chunkIndex,
                            socketOutFalse,
                            new Dictionary<int, KVObject>(registerConstValueMap),
                            new Dictionary<int, GraphSocket>(registerOutputSocketMap),
                            destInstructionIdxFalse,
                            firstInsturctionAfterBranches == -1 ? finalEndingInstructionIdx : firstInsturctionAfterBranches
                        );

                        // create even if we're returning, cause the socket still could be connected to further actions
                        // if the current flow was a subroutine
                        previousActionOutSocket = node.CreateSocketOut<Flow>("Finished");
                        if (firstInsturctionAfterBranches != -1)
                        {
                            instructionIdx = firstInsturctionAfterBranches - 1; // next iteration will +1 this
                        }
                        else
                        {
                            stopProcessing = true;
                        }

                        View.AddNode(node);

                        break;
                    }
                default:
                    {
                        if (!flowInstructions.Contains(instrType))
                        {
                            var reg0 = instruction.GetInt32Property("m_nReg0");
                            var reg1 = instruction.GetInt32Property("m_nReg1");
                            var reg2 = instruction.GetInt32Property("m_nReg2");

                            if (reg0 == -1) // nothing to do
                            {
                                continue;
                            }

                            var node = new Node(null)
                            {
                                Name = instrNameString,
                                NodeType = "Instruction",
                            };

                            if (reg1 != -1)
                            {
                                AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, reg1, "arg1");
                            }

                            if (reg2 != -1)
                            {
                                AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, reg2, "arg2");
                            }

                            if (reg1 == -1 && reg2 == -1)
                            {
                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                                AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, reg0, "arg");
                            }

                            // create output socket for this node, and store it for future connections
                            var socketOut = node.CreateSocketOutFromValueType("retval", GetValueTypeFromRegister(chunkIndex, reg0));
                            registerOutputSocketMap[reg0] = socketOut;
                            View.AddNode(node);
                        }

                        break;
                    }

            }
        }
        return previousActionOutSocket;
    }

    private void GeneratePossibleOutflowsForCell(
        Node node,
        int cellIdx,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, GraphSocket> registerOutputSocketMap,
        int maxInstructionIdx,
        HashSet<string> ignoredNames // if we want to handle some outflows explicitly
    )
    {
        var outflows = GetCellOutflows(cellIdx);
        foreach (var outflow in outflows)
        {
            if (!ignoredNames.Contains(outflow.sourceOutflowName))
            {
                AddOutflowSocket(node, outflow, outflow.sourceOutflowName, registerConstValueMap, registerOutputSocketMap, maxInstructionIdx);
            }
        }
    }

    // Retrieves m_nFlowNodeID from m_InstructionDebugInfos for a particular instruction
    // For older files retrieve the ID from m_InstructionEditorIDs
    private int GetInstructionFlowId(int chunkId, int instructionIdx)
    {
        var chunk = chunks[chunkId];
        if (chunk.TryGetValue("m_InstructionDebugInfos", out var debugInfos))
        {
            return debugInfos.AsArraySpan()[instructionIdx].GetInt32Property("m_nFlowNodeID");
        }

        if (chunk.TryGetValue("m_InstructionEditorIDs", out var editorIds))
        {
            return editorIds.AsArraySpan()[instructionIdx].ToInt32(CultureInfo.InvariantCulture);
        }

        Log.Error(nameof(PulseGraphViewer),
            $"Failed to retrieve flow node ID for chunk {chunkId} instruction {instructionIdx}. No m_InstructionDebugInfos, or m_InstructionEditorIDs found in chunk definition.");
        return -1; // Fine to return -1, as it is not fully necessary to make everything work and that value can also appear normally.
    }

    // Generates outflows and labels for specific cells, this is needed as each one can have very different meaning or behavior.
    // Additionally, handle outflows. Explicitly, or automatically.
    private void PopulateCellAndTraverseOutflows(
        Node node,
        int cellIdx,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, GraphSocket> registerOutputSocketMap,
        int maxInstructionIdx
    )
    {
        HashSet<string> processedOutflowNames = [];
        var cellCategory = GetCellCategory(cellIdx);
        var cellType = GetCellType(cellIdx, out var cellName);

        switch (cellCategory)
        {
            case CellCategory.Inflow:
                {
                    // here we assume that wait is going to be processed sequentially, not out of order, even though it's theoretically possible.
                    if (cellType == CellType.Wait)
                    {
                        var wakeResume = cells[cellIdx]["m_WakeResume"];
                        var destChunk = wakeResume.GetInt32Property("m_nDestChunk");
                        var destInstructionIdx = wakeResume.GetInt32Property("m_nInstruction");

                        var outputSocket = node.CreateSocketOut<Flow>("OnFinished");
                        TraverseOutflow(destChunk, destInstructionIdx, maxInstructionIdx, outputSocket, registerConstValueMap, registerOutputSocketMap);
                        processedOutflowNames.Add("m_WakeResume");
                    }
                    break;
                }
            case CellCategory.Step:
                {
                    switch (cellType)
                    {
                        case CellType.PublicOutput:
                            {
                                var outputIndex = cells[cellIdx].GetInt32Property("m_OutputIndex");
                                if (outputIndex == -1)
                                {
                                    break;
                                }

                                var publicOutput = publicOutputs[outputIndex];
                                var outputName = publicOutput.GetStringProperty("m_Name", $"<NAME UNKNOWN>");
                                var outputDesc = publicOutput.GetStringProperty("m_Description", "");

                                node.AddText($"Public Output: {outputName}");
                                node.AddText($"Description: {outputDesc}");
                                break;
                            }

                    }
                    break;
                }
            case CellCategory.Unspecified:
                {
                    switch (cellType)
                    {
                        case CellType.Timeline:
                            {
                                var timelineEvents = cells[cellIdx].GetArray("m_TimelineEvents");
                                foreach (var timelineEvent in timelineEvents)
                                {
                                    var eventOutflow = (PulseOutflowConnection?)timelineEvent["m_EventOutflow"];
                                    if (eventOutflow is null || eventOutflow.destChunk == -1)
                                    {
                                        continue;
                                    }

                                    var timeFromPrevious = timelineEvent.GetFloatProperty("m_flTimeFromPrevious");
                                    var socketLabel = $"(Time from prev: {timeFromPrevious}s) | {eventOutflow.sourceOutflowName}";
                                    AddOutflowSocket(node, eventOutflow, socketLabel, registerConstValueMap, registerOutputSocketMap, maxInstructionIdx);
                                    processedOutflowNames.Add(eventOutflow.sourceOutflowName);
                                }
                                break;
                            }
                    }
                    break;
                }
        }

        GeneratePossibleOutflowsForCell(node, cellIdx, registerConstValueMap, registerOutputSocketMap, maxInstructionIdx, processedOutflowNames);
    }

    private void CreateGraph()
    {
        loopInstructionMap = FindGraphInstructionCycles();

        Dictionary<int, string> chunkFunctionName = [];
        var currentUnknownNamedFuncNumber = 0;

        // Inflow cells
        for (var cellIdx = 0; cellIdx < cells.Count; cellIdx++)
        {
            var cellCategory = GetCellCategory(cellIdx);
            var cellType = GetCellType(cellIdx, out var cellName);
            var cellNode = new Node(null)
            {
                Name = cellName,
                NodeType = cellCategory.ToString(),
            };

            switch (cellCategory)
            {
                case CellCategory.Inflow:
                    {
                        if (!cells[cellIdx].ContainsKey("m_EntryChunk"))
                        {
                            continue;
                        }

                        Dictionary<int, GraphSocket> registerSocketOutputMap = [];
                        var entryChunkIdx = cells[cellIdx].GetInt32Property("m_EntryChunk");

                        var outputSocket = cellNode.CreateSocketOut<Flow>("");

                        if (cells[cellIdx].TryGetValue("m_RegisterMap", out var registerMap))
                        {
                            TryAddRegisterMapOutParams(cellNode, entryChunkIdx, registerSocketOutputMap, registerMap);
                        }

                        TraverseNodesForChunk(
                            entryChunkIdx,
                            outputSocket,
                            [],
                            registerSocketOutputMap
                        );
                        chunkFunctionName.Add(entryChunkIdx, cells[cellIdx].GetStringProperty("m_MethodName"));

                        AddFilteredCellDetails(cellNode, cellIdx);

                        View.AddNode(cellNode);
                        break;
                    }
            }
        }

        if (chunkFunctionName.Keys.Count < cells.Count)
        {
            // Resolve chunks that are not referenced by any cell.
            for (var chunkId = 0; chunkId < chunks.Count; chunkId++)
            {
                if (!chunkFunctionName.ContainsKey(chunkId))
                {
                    var newName = $"Unnamed_{++currentUnknownNamedFuncNumber}";
                    var cellNode = new Node(null)
                    {
                        Name = "Function",
                        NodeType = ""
                    };

                    var outputSocket = cellNode.CreateSocketOut<Flow>("");
                    chunkFunctionName.Add(chunkId, newName);
                    cellNode.AddText(newName);

                    TraverseNodesForChunk(chunkId, outputSocket, [], []);
                    View.AddNode(cellNode);
                }
            }
        }

        // General info as a node
        var graphInfoNode = new Node(null)
        {
            Name = "Graph info",
            NodeType = "",
        };

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
        View.AddNode(graphInfoNode);

        // Variable definitions as separate nodes, cause there's no specific pane for displaying them.
        foreach (var variable in variables)
        {
            var node = new Node(variable)
            {
                Name = variable.GetStringProperty("m_Name"),
                NodeType = "Variable",
            };
            node.AddText($"Type: {variable.GetStringProperty("m_Type")}");
            node.AddText($"Initial value: {variable["m_DefaultValue"]}");
            node.AddText($"Keys source: {variable.GetStringProperty("m_nKeysSource")}");
            if (variable.GetBooleanProperty("m_bIsObservable"))
            {
                node.AddText("Observable");
            }

            var description = variable.GetStringProperty("m_Description");
            if (!string.IsNullOrEmpty(description))
            {
                node.AddText(description);
            }
            View.AddNode(node);
        }

        // Resolve call nodes to display the target function name
        foreach (var callNodeInfo in callNodesToResolve)
        {
            var targetChunk = callNodeInfo.targetChunk;
            var methodNameToCall = chunkFunctionName[targetChunk];
            callNodeInfo.node.AddText($"Method: {methodNameToCall}");
            View.AddNode(callNodeInfo.node);
        }

        View.LayoutNodesPacked();

        View.Legend.AddRange(
        [
            new("Flow", GraphHue.Neutral, GraphLegendKind.Wire),
            new("String value", GraphHue.Green, GraphLegendKind.Wire),
            new("Number value", GraphHue.Amber, GraphLegendKind.Wire),
            new("Bool value", GraphHue.Orange, GraphLegendKind.Wire),
            new("Other value", GraphHue.Slate, GraphLegendKind.Wire),
            new("Variable link", GraphHue.Indigo, GraphLegendKind.DashedWire),
        ]);
    }
    #region Nodes
    class Node : KVGraphNode
    {
        public Node(KVObject? data) : base(data)
        {
        }

        public GraphSocket CreateSocketIn<T>(string text) where T : struct => AddInput(text, HueOf(typeof(T)));
        public GraphSocket CreateSocketOut<T>(string text) where T : struct => AddOutput(text, HueOf(typeof(T)));
        public GraphSocket CreateSocketInFromValueType(string text, PulseValueType valueType) => AddInput(text, HueOfPval(valueType));
        public GraphSocket CreateSocketOutFromValueType(string text, PulseValueType valueType) => AddOutput(text, HueOfPval(valueType));
    }

    #endregion Nodes
}
