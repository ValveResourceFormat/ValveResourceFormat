using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;
using Svg.Skia;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.Blocks.ResourceIntrospectionManifest.ResourceDiskEnum;

namespace GUI.Types.Graphs;

internal class PulseGraphViewer : GLNodeGraphViewer
{
    private static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);
    public static SKColor NodeColor { get; set; }
    public static SKColor NodeTextColor { get; set; }

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
    private readonly Dictionary<int, Dictionary<int, SocketIn>> instructionInputActionSocketMap = [];
    private readonly List<NodeCallInfo> callNodesToResolve = [];
    private Dictionary<int, HashSet<List<int>>> loopInstructionMap = [];

    struct NodeCallInfo
    {
        public int targetChunk;
        public Node node;
    }

    struct PulseOutflowConnection
    {
        public string sourceOutflowName;
        public int destChunk;
        public int destInstructionIdx;
    }

    #region Socket types
    private static SKColor FlowColor { get; set; }
    private struct Flow;
    private static SKColor ValueStringColor { get; set; }
    private struct ValueString;
    private static SKColor ValueNumberColor { get; set; }
    private struct ValueNumber;
    private static SKColor ValueBoolColor { get; set; }
    private struct ValueBool;
    private static SKColor ValueOtherColor { get; set; }
    private struct ValueOther;
    #endregion Socket types

    public PulseGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, CreateAndConfigureNodeGraph(data, out var graphDef))
    {
        graphDefinition = graphDef;

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

    // Stringify a KVObject for our purposes
    private static string StringifyKVObject(KVObject obj)
    {
        switch (obj.ValueType)
        {
            case KVValueType.String:
                return $"\"{obj}\"";
            case KVValueType.Boolean:
                return obj.ToBoolean(CultureInfo.InvariantCulture) ? "true" : "false";
            case KVValueType.Array:
                {
                    var list = obj.AsArraySpan();
                    StringBuilder sb = new();
                    sb.Append('[');
                    var firstElem = true;
                    foreach (var elem in list)
                    {
                        if (!firstElem)
                        {
                            sb.Append(", ");
                        }
                        firstElem = false;

                        sb.Append(StringifyKVObject(elem));
                    }
                    sb.Append(']');
                    return sb.ToString();
                }
            case KVValueType.Int16:
            case KVValueType.UInt16:
            case KVValueType.Int32:
            case KVValueType.UInt32:
            case KVValueType.Int64:
            case KVValueType.UInt64:
            case KVValueType.FloatingPoint:
            case KVValueType.FloatingPoint64:
            default:
                return obj.ToString(CultureInfo.InvariantCulture);
        }
    }

    private bool TryAddRegisterMapOutParams(
        Node node,
        int chunkIndex,
        Dictionary<int, SocketOut> registerOutputSocketMap,
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
            node.AddText($"{kvPair.Key} = {StringifyKVObject(kvPair.Value)}");
        }
    }

    private void TraverseOutflow(
        int destChunk,
        int destInstructionIdx,
        int maxInstructionIdx, // non-inclusive, use when there's a need to limit the range inside a loop
        SocketOut outputSocket,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerOutputSocketMap)
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
            new Dictionary<int, SocketOut>(registerOutputSocketMap),
            destInstructionIdx,
            maxInstructionIdx
        );
    }

    private void AddOutflowSocket(
        Node node,
        KVObject outflow,
        string socketLabel,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerOutputSocketMap,
        int maxInstructionIdx
    )
    {
        var destChunk = outflow.GetInt32Property("m_nDestChunk");
        var destInstructionIdx = outflow.GetInt32Property("m_nInstruction");
        if (destChunk == -1 || destInstructionIdx == -1)
        {
            return;
        }

        var outputSocket = node.CreateSocketOut<Flow>(socketLabel);
        TraverseOutflow(destChunk, destInstructionIdx, maxInstructionIdx, outputSocket, registerConstValueMap, registerOutputSocketMap);
    }

    private static NodeGraphControl CreateAndConfigureNodeGraph(KVObject data, out KVObject graphDef)
    {
        graphDef = data;
        var nodeGraph = new NodeGraphControl
        {
            GridStyle = NodeGraphControl.EGridStyle.Dots,

            CanvasBackgroundColor = new SKColor(10, 10, 10)
        };
        // referencing pulse_scene_styles_v2.vdata, slightly modified:
        NodeColor = new SKColor(80, 80, 80);
        NodeTextColor = SKColor.Parse("#FFFFFF");
        FlowColor = SKColor.Parse("#999999"); // $data_type_flow
        ValueStringColor = SKColor.Parse("#7AD691"); // $data_type_string
        ValueNumberColor = SKColor.Parse("#DDB85D"); // $data_type_number
        ValueBoolColor = SKColor.Parse("#D15226"); // $data_type_bool
        ValueOtherColor = SKColor.Parse("#32505D"); // $data_type_other

        nodeGraph.GridColor = SKColors.White;
        if (Themer.CurrentTheme == Themer.AppTheme.Dark)
        {
            nodeGraph.CanvasBackgroundColor = ToSKColor(Themer.CurrentThemeColors.AppMiddle);
            NodeColor = ToSKColor(Themer.CurrentThemeColors.AppSoft);
            nodeGraph.GridColor = ToSKColor(Themer.CurrentThemeColors.ContrastSoft);
        }

        NodeGraphControl.AddTypeColorPair<Flow>(FlowColor);
        NodeGraphControl.AddTypeColorPair<ValueString>(ValueStringColor);
        NodeGraphControl.AddTypeColorPair<ValueNumber>(ValueNumberColor);
        NodeGraphControl.AddTypeColorPair<ValueBool>(ValueBoolColor);
        NodeGraphControl.AddTypeColorPair<ValueOther>(ValueOtherColor);

        return nodeGraph;
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

    private SocketOut CreateSequentialActionSockets(Node node, SocketOut previousActionOutSocket, int chunkIdx, int instructionIdx)
    {
        var socketIn = node.CreateSocketIn<Flow>("");
        nodeGraph.Connect(previousActionOutSocket, socketIn);
        instructionInputActionSocketMap[chunkIdx][instructionIdx] = socketIn;

        var socketOut = node.CreateSocketOut<Flow>("");
        return socketOut;
    }

    private void AddNodeRegisterInput(
        Node node,
        int chunkIndex,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerSocketOutputMap,
        int regIndex,
        string name)
    {
        if (registerSocketOutputMap.TryGetValue(regIndex, out var regOutSocket))
        {
            var argInputSocket = node.CreateSocketInFromValueType(name, GetValueTypeFromRegister(chunkIndex, regIndex));
            nodeGraph.Connect(regOutSocket, argInputSocket);
            return;
        }
        else if (registerConstValueMap.TryGetValue(regIndex, out var obj))
        {
            node.AddText($"{name} = {StringifyKVObject(obj)}");
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

    // Returns pairs of chunk and instruction for all potential outflows of the provied cell.
    private List<PulseOutflowConnection> GetCellOutflows(int cellIdx)
    {
        List<PulseOutflowConnection> outflows = [];
        static void GetCellOutflowsRecurse(KVObject obj, List<PulseOutflowConnection> outflowList)
        {
            if (obj.TryGetValue("m_SourceOutflowName", out var sourceOutflowName)
                && obj.TryGetValue("m_nDestChunk", out var destChunk)
                && obj.TryGetValue("m_nInstruction", out var destInstruction))
            {
                outflowList.Add(new PulseOutflowConnection
                {
                    sourceOutflowName = sourceOutflowName.ToString(CultureInfo.InvariantCulture),
                    destChunk = destChunk.ToInt32(CultureInfo.InvariantCulture),
                    destInstructionIdx = destInstruction.ToInt32(CultureInfo.InvariantCulture),
                });
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

    private void CreateInputsFromRegisterMap(
        Node node,
        int chunkIndex,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerSocketOutputMap,
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
                node.AddText($"{regName} = {StringifyKVObject(regValue)}");
            }
            else if (registerSocketOutputMap.TryGetValue(regIdx, out var regOutSocket))
            {
                var argInputSocket = node.CreateSocketInFromValueType(regName, GetValueTypeFromRegister(chunkIndex, regIdx));
                nodeGraph.Connect(regOutSocket, argInputSocket);
            }
            else
            {
                node.AddText($"{regName} = <FAILED TO RESOLVE>");
                Log.Warn(nameof(PulseGraphViewer), $"Failed to find register id={regIdx} at chunk={chunkIndex} which was expected to be generated already.");
            }
        }
    }

    private SocketOut? TraverseNodesForChunk(
        int chunkIndex,
        SocketOut sourceActionOutSocket,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerOutputSocketMap,
        int startingInstructionIdx = 0,
        int endingInstructionIdx = int.MaxValue, // non-inclusive
        bool ignoreActions = false)
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
                        var newRegisterOutputSocketMap = new Dictionary<int, SocketOut>(registerOutputSocketMap);
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
                        nodeGraph.AddNode(doWhileNode);
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
                            Log.Warn(nameof(PulseGraphViewer), $"Could not find conditional jump instruction for loop starting at instruction {loopStart} to {loopEndInstr} in chunk {chunkIndex}");
                            // Loop can happen with async calls, but it's safe to proceed.
                            //instructionIdx = loopEnd + 1;
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

                        if (regStep == -1)
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

                        Node? forLoopNode = null;
                        try
                        {
                            forLoopNode = new Node(null)
                            {
                                Name = "Loop",
                                NodeType = "Flow control",
                            };

                            var loopSocketIn = forLoopNode.CreateSocketIn<Flow>("");
                            nodeGraph.Connect(previousActionOutSocket, loopSocketIn);
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
                                    // ASSUME that we got this increment from a constant (previous instruction)
                                    // TODO: actually follow the register chain until we get a const, then connect these operations to the increment socket

                                    var opReg0 = instrLastInLoop.GetInt32Property("m_nReg0");
                                    var opReg1 = instrLastInLoop.GetInt32Property("m_nReg1");
                                    var opReg2 = instrLastInLoop.GetInt32Property("m_nReg2");

                                    // use the value that's not the output one, so the increment
                                    var incrementReg = opReg2;
                                    if (opReg1 != opReg0)
                                    {
                                        incrementReg = opReg1;
                                    }

                                    if (registers[incrementReg].GetInt32Property("m_nWrittenByInstruction") < loopStart) // already calculated, connect it
                                    {
                                        AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, incrementReg, "Increment");
                                    }
                                    else if (TryGetConstantValueFromId(instructions[loopEnd - 2].GetInt32Property("m_nConstIdx"), out var constIncrement))
                                    {
                                        forLoopNode.AddText($"Increment = {StringifyKVObject(constIncrement)}");
                                        loopOperationEndInstructionIdx = loopEnd - 2;
                                    }
                                    else
                                    {
                                        forLoopNode.AddMessage("Could not find increment");
                                    }
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
                                forLoopNode.AddMessage("Could not determine the loop's increment");
                            }

                            AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, condRegister, "Loop condition");

                            if (loopOperationEndInstructionIdx == loopJumpOutInstructionIdx)
                            {
                                Log.Info(nameof(PulseGraphViewer), $"Potentially empty loop (chunk={chunkIndex}, instruction={loopOperationEndInstructionIdx})");
                            }

                            //Log.Info(nameof(PulseGraphViewer), $"Chunk: {chunkIndex} loopJumpOutInstructionId={loopJumpOutInstructionIdx} -> {loopJumpOutInstruction.GetInt32Property("m_nDestInstruction")} | loopOperationEndInstructionId={loopOperationEndInstructionIdx}");

                            var socketOutLoopAction = forLoopNode.CreateSocketOut<Flow>("Loop");
                            TraverseNodesForChunk(
                                chunkIndex,
                                socketOutLoopAction,
                                new Dictionary<int, KVObject>(registerConstValueMap),
                                new Dictionary<int, SocketOut>(registerOutputSocketMap),
                                loopJumpOutInstructionIdx + 1,
                                loopOperationEndInstructionIdx
                            );
                            previousActionOutSocket = forLoopNode.CreateSocketOut<Flow>("Finished");

                            forLoopNode.Calculate();
                            nodeGraph.AddNode(forLoopNode);
                            forLoopNode = null;
                        }
                        finally
                        {
                            forLoopNode?.Dispose();
                        }
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
                        Node? node = null;
                        try
                        {
                            node = new Node(null)
                            {
                                Name = funcName,
                                NodeType = "Function",
                            };
                            // If an action without outputs then create a new output action and continue.
                            if (!TryAddRegisterMapOutParams(node, chunkIndex, registerOutputSocketMap, registerMap))
                            {
                                if (ignoreActions)
                                {
                                    break;
                                }

                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            }
                            // it will be the color of the first output socket
                            node.UpdateTypeColorFromOutput();

                            CreateInputsFromRegisterMap(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, registerMap);

                            node.Calculate();
                            nodeGraph.AddNode(node);
                            node = null;
                        }
                        finally
                        {
                            node?.Dispose();
                        }
                        break;
                    }
                case InstructionCode.CELL_INVOKE:
                    {
                        var invokeIndex = instruction.GetInt32Property("m_nInvokeBindingIndex");
                        var binding = invokeBindings[invokeIndex];
                        var registerMap = binding["m_RegisterMap"];

                        var funcName = binding.GetStringProperty("m_FuncName");
                        var cellIndex = binding.GetInt32Property("m_nCellIndex");
                        var cell = cells[cellIndex];
                        var cellType = GetCellType(cellIndex, out var cellName);
                        var cellCategory = GetCellCategory(cellIndex);

                        var funcNameSplitIdx = funcName.IndexOf("::", StringComparison.InvariantCulture);
                        Node? node = null;
                        try
                        {
                            node = new Node(null)
                            {
                                Name = cellName,
                                // show name after '::' separator, if can't find then show full name
                                NodeType = funcName[(funcNameSplitIdx >= 0 ? (funcNameSplitIdx + 2) : 0)..],
                            };
                            // If an action without outputs then create a new output action and continue.
                            if (!TryAddRegisterMapOutParams(node, chunkIndex, registerOutputSocketMap, registerMap))
                            {
                                if (ignoreActions)
                                {
                                    break;
                                }

                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            }
                            // it will be the color of the first output socket
                            node.UpdateTypeColorFromOutput();

                            AddFilteredCellDetails(node, cellIndex);

                            CreateInputsFromRegisterMap(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, registerMap);
                            PopulateSpecificCell(node, cellIndex, registerConstValueMap, registerOutputSocketMap, finalEndingInstructionIdx);

                            node.Calculate();
                            nodeGraph.AddNode(node);
                            node = null;
                        }
                        finally
                        {
                            node?.Dispose();
                        }
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
                        if (TryGetVariableNameFromId(varIndex, out var name))
                        {
                            node.AddText(name);
                        }
                        else
                        {
                            node.AddText($"<UNKNOWN m_nVar={varIndex}>");
                            Log.Warn(nameof(PulseGraphViewer), $"Failed to retrieve variable name of ID={varIndex}. Invalid graph definition?");
                        }
                        var outSocket = node.CreateSocketOutFromValueType("retval", GetValueTypeFromRegister(chunkIndex, regIndex));
                        registerOutputSocketMap[regIndex] = outSocket;
                        node.UpdateTypeColorFromOutput();

                        node.Calculate();
                        nodeGraph.AddNode(node);
                        break;
                    }
                case InstructionCode.SET_VAR:
                    {
                        if (ignoreActions)
                        {
                            break;
                        }

                        var varIndex = instruction.GetInt32Property("m_nVar");
                        var regIndex = instruction.GetInt32Property("m_nReg0");
                        var node = new Node(null)
                        {
                            Name = "Set Variable",
                            NodeType = "Instruction",
                        };
                        previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);

                        if (TryGetVariableNameFromId(varIndex, out var name))
                        {
                            node.AddText(name);
                        }
                        else
                        {
                            node.AddText($"<UNKNOWN m_nVar={varIndex}>");
                            Log.Warn(nameof(PulseGraphViewer), $"Failed to retrieve variable name of ID={varIndex}. Invalid graph definition?");
                        }
                        AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, regIndex, "value");

                        node.Calculate();
                        nodeGraph.AddNode(node);
                        break;
                    }
                case InstructionCode.PULSE_CALL_SYNC:
                case InstructionCode.PULSE_CALL_ASYNC_FIRE:
                    {
                        var callTargetChunk = instruction.GetInt32Property("m_nChunk");
                        var callDestInstructionIdx = instruction.GetInt32Property("m_nDestInstruction");
                        if (callTargetChunk != chunkIndex || callDestInstructionIdx <= 0)
                        {
                            if (ignoreActions)
                            {
                                break;
                            }

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
                                new Dictionary<int, SocketOut>(registerOutputSocketMap),
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
                        if (ignoreActions)
                        {
                            break;
                        }

                        var regIndex = instruction.GetInt32Property("m_nReg0");
                        Node? node = null;
                        try
                        {
                            node = new Node(null)
                            {
                                Name = "Return Value",
                                NodeType = "Flow",
                            };
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, regIndex, "value");
                            node.Calculate();
                            nodeGraph.AddNode(node);
                            node = null;
                        }
                        finally
                        {
                            node?.Dispose();
                        }
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
                            new Dictionary<int, SocketOut>(registerOutputSocketMap),
                            destInstructionIdx,
                            finalEndingInstructionIdx);
                        break;
                    }
                case InstructionCode.JUMP_COND:
                    {
                        var reg0 = instruction.GetInt32Property("m_nReg0");
                        Node? node = null;
                        try
                        {
                            node = new Node(null)
                            {
                                Name = "If",
                                NodeType = "Flow control",
                            };
                            var socketIn = node.CreateSocketIn<Flow>("");
                            nodeGraph.Connect(previousActionOutSocket, socketIn);
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
                                new Dictionary<int, SocketOut>(registerOutputSocketMap),
                                destInstructionIdxTrue,
                                firstInsturctionAfterBranches == -1 ? finalEndingInstructionIdx : firstInsturctionAfterBranches
                            );

                            var socketOutFalse = node.CreateSocketOut<Flow>("False");
                            TraverseNodesForChunk(
                                chunkIndex,
                                socketOutFalse,
                                new Dictionary<int, KVObject>(registerConstValueMap),
                                new Dictionary<int, SocketOut>(registerOutputSocketMap),
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

                            if (!ignoreActions)
                            {
                                node.Calculate();
                                nodeGraph.AddNode(node);
                                node = null;
                            }
                        }
                        finally
                        {
                            node?.Dispose();
                        }

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
                            node.UpdateTypeColorFromOutput();
                            node.Calculate();
                            nodeGraph.AddNode(node);
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
        Dictionary<int, SocketOut> registerOutputSocketMap,
        int maxInstructionIdx
    )
    {
        var cell = cells[cellIdx];
        foreach (var outflow in cell.Values)
        {
            if (outflow.TryGetValue("m_SourceOutflowName", out var outflowName))
            {
                AddOutflowSocket(node, outflow, outflowName.ToString(CultureInfo.InvariantCulture), registerConstValueMap, registerOutputSocketMap, maxInstructionIdx);
            }
        }
    }

    // Retrives m_nFlowNodeID from m_InstructionDebugInfos for a particular instruction
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
    private void PopulateSpecificCell(
        Node node,
        int cellIdx,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerOutputSocketMap,
        int maxInstructionIdx
    )
    {
        var cellCategory = GetCellCategory(cellIdx);
        var cellType = GetCellType(cellIdx, out var cellName);

        switch (cellCategory)
        {
            case CellCategory.Inflow:
                {
                    // here we assume that wait is going to be processed sequentially, not out of order, even though it's theorithically possible.
                    if (cellType == CellType.Wait)
                    {
                        var wakeResume = cells[cellIdx]["m_WakeResume"];
                        var destChunk = wakeResume.GetInt32Property("m_nDestChunk");
                        var destInstructionIdx = wakeResume.GetInt32Property("m_nInstruction");

                        var outputSocket = node.CreateSocketOut<Flow>("OnFinished");
                        TraverseOutflow(destChunk, destInstructionIdx, maxInstructionIdx, outputSocket, registerConstValueMap, registerOutputSocketMap);
                    }
                    break;
                }
            case CellCategory.Outflow:
                {
                    switch (cellType)
                    {
                        case CellType.CycleRandom:
                        case CellType.CycleShuffled:
                        case CellType.CycleOrdered:
                            {
                                var outputs = cells[cellIdx].GetArray("m_Outputs");
                                foreach (var output in outputs)
                                {
                                    var outflowName = output.GetStringProperty("m_SourceOutflowName");
                                    AddOutflowSocket(node, output, outflowName, registerConstValueMap, registerOutputSocketMap, maxInstructionIdx);
                                }
                                break;
                            }

                    }
                    GeneratePossibleOutflowsForCell(node, cellIdx, registerConstValueMap, registerOutputSocketMap, maxInstructionIdx);

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
                                    var eventOutflow = timelineEvent["m_EventOutflow"];
                                    var destChunk = eventOutflow.GetInt32Property("m_nDestChunk");
                                    if (destChunk == -1)
                                    {
                                        continue;
                                    }

                                    var outflowName = eventOutflow.GetStringProperty("m_SourceOutflowName");

                                    var timeFromPrevious = timelineEvent.GetFloatProperty("m_flTimeFromPrevious");
                                    var socketLabel = $"(Time from prev: {timeFromPrevious}s) | {outflowName}";
                                    AddOutflowSocket(node, eventOutflow, socketLabel, registerConstValueMap, registerOutputSocketMap, maxInstructionIdx);
                                }
                                break;
                            }
                    }
                    // Some cells don't have Outflow in the name, but still have outflows.
                    GeneratePossibleOutflowsForCell(node, cellIdx, registerConstValueMap, registerOutputSocketMap, maxInstructionIdx);
                    break;
                }
        }
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
            Node? cellNode = null;
            try
            {
                cellNode = new Node(null)
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

                            Dictionary<int, SocketOut> registerSocketOutputMap = [];
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

                            cellNode.Calculate();
                            nodeGraph.AddNode(cellNode);
                            cellNode = null;
                            break;
                        }
                }
            }
            finally
            {
                cellNode?.Dispose();
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
                    cellNode.Calculate();
                    nodeGraph.AddNode(cellNode);
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
        nodeGraph.AddNode(graphInfoNode);

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
            node.Calculate();
            nodeGraph.AddNode(node);
        }

        // Resolve call nodes to display the target function name
        foreach (var callNodeInfo in callNodesToResolve)
        {
            var targetChunk = callNodeInfo.targetChunk;
            var methodNameToCall = chunkFunctionName[targetChunk];
            callNodeInfo.node.AddText($"Method: {methodNameToCall}");
            callNodeInfo.node.Calculate();
            nodeGraph.AddNode(callNodeInfo.node);
        }

        nodeGraph.LayoutNodesSequential();
    }
    #region Nodes
    class Node : AbstractNode
    {
        public KVObject? Data { get; set; }
        private List<string> Messages { get; set; } = [];
        public Node(KVObject? data)
        {
            Data = data;
            BaseColor = NodeColor;
            TextColor = NodeTextColor;
            HeaderColor = ToSKColor(ControlPaint.Light(Color.FromArgb(NodeColor.Red, NodeColor.Green, NodeColor.Blue)));
            HeaderTextColor = new SKColor(5, 5, 5);
            HeaderTypeColor = new SKColor(25, 25, 25);
        }

        public void AddMessage(string message)
        {
            AddSpace();
            Messages.Add(message);
        }

        public void UpdateTypeColorFromOutput()
        {
            var outputSocket = Sockets.OfType<SocketOut>().FirstOrDefault();

            if (outputSocket != null)
            {
                var typeColor = NodeGraphControl.GetColorByType(outputSocket.ValueType);
                if (typeColor != SKColor.Empty)
                {
                    HeaderColor = typeColor;
                }
            }
        }

        public void AddSpace() => CreateTextSocket<string>(string.Empty);
        public void AddText(string text) => CreateTextSocket<string>(text);

        private void CreateTextSocket<T>(string text)
        {
            var socket = new SocketIn(typeof(T), text, this, false)
            {
                DisplayOnly = true
            };
            Sockets.Add(socket);
        }
        private static Type SocketValueTypeFromPvalType(PulseValueType valueType)
        {
            return valueType switch
            {
                PulseValueType.PVAL_INT or PulseValueType.PVAL_FLOAT => typeof(ValueNumber),
                PulseValueType.PVAL_STRING => typeof(ValueString),
                PulseValueType.PVAL_BOOL => typeof(ValueBool),
                PulseValueType.PVAL_VOID => typeof(ValueOther),
                _ => typeof(ValueOther)
            };
        }

        public SocketIn CreateSocketIn<T>(string text) where T : struct
        {
            var socket = new SocketIn(typeof(T), text, this, hub: true);
            Sockets.Add(socket);
            return socket;
        }
        public SocketOut CreateSocketOut<T>(string text) where T : struct
        {
            var socket = new SocketOut(typeof(T), text, this);
            Sockets.Add(socket);
            return socket;
        }
        public SocketIn CreateSocketInFromValueType(string text, PulseValueType valueType)
        {
            var type = SocketValueTypeFromPvalType(valueType);
            var socket = new SocketIn(type, text, this, hub: true);
            Sockets.Add(socket);
            return socket;
        }
        public SocketOut CreateSocketOutFromValueType(string text, PulseValueType valueType)
        {
            var type = SocketValueTypeFromPvalType(valueType);
            var socket = new SocketOut(type, text, this);
            Sockets.Add(socket);
            return socket;
        }

        private static readonly SKFont ArialFont = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal).ToFont(15f);

        public override void Draw(SKCanvas canvas, bool isPrimarySelected, bool isConnected, bool isHovered)
        {
            base.Draw(canvas, isPrimarySelected, isConnected, isHovered);
            // Display messages

            if (Messages.Count == 0)
            {
                return;
            }

            // Get icon from cache
            IconCache.TryGetValue("About", out var iconToUse);
            const int iconSize = 16;
            for (var i = 0; i < Messages.Count; i++)
            {
                var yOffset = (Sockets.Count > 1 ? 55 : 45) + (i * 25);
                var position = new SKPoint
                {
                    X = Location.X + 3,
                    Y = Location.Y + yOffset
                };

                // Draw the icon
                if (iconToUse != null)
                {
                    var picture = iconToUse.Picture;
                    Debug.Assert(picture is not null);

                    var scaleMatrix = SKMatrix.CreateScale(iconSize / picture.CullRect.Width, iconSize / picture.CullRect.Height);
                    canvas.DrawPicture(picture, position);
                }

                // Draw the text next to the icon
                var textPosition = new SKPoint
                {
                    X = position.X + iconSize + 6,
                    Y = position.Y + ArialFont.Size + 2
                };

                using var paint = new SKPaint { Color = new(78, 145, 217), IsAntialias = true };
                canvas.DrawText(Messages[i], textPosition.X, textPosition.Y, ArialFont, paint);
            }
        }

        private static readonly Dictionary<string, SKSvg> IconCache = [];

        static Node()
        {
            string[] icons =
            [
                "About",
            ];

            foreach (var iconName in icons)
            {
                using var svgResource = Program.Assembly.GetManifestResourceStream($"GUI.Icons.{iconName}.svg");
                Debug.Assert(svgResource is not null);

                var svg = new SKSvg();
                svg.Load(svgResource);
                IconCache[iconName] = svg;
            }
        }
    }

    #endregion Nodes

}
