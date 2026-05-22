using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Svg.Skia;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;

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
        ListenForEntityOutput,
        ListenForAnimgraphTag,
        PlaySequence,
        PlayVCD,
        PlayVOLine,
        ScriptedSequence,
        WaitForCursorsWithTag,
        WaitForObservable,
        IntervalTimer,
        PublicOutput
    }

    enum InstructionType
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

    private readonly HashSet<InstructionType> flowInstructions =
    [
        InstructionType.IMMEDIATE_HALT,
        InstructionType.RETURN_VOID,
        InstructionType.RETURN_VALUE,
        InstructionType.NOP,
        InstructionType.JUMP,
        InstructionType.JUMP_COND,
        InstructionType.CHUNK_LEAP,
        InstructionType.CHUNK_LEAP_COND,
    ];

    private IReadOnlyList<KVObject> cells;
    private IReadOnlyList<KVObject> chunks;
    private IReadOnlyList<KVObject> invokeBindings;
    private IReadOnlyList<KVObject> domainValues;
    private IReadOnlyList<KVObject> constants;
    private IReadOnlyList<KVObject> variables;
    private IReadOnlyList<KVObject> publicOutputs;
    private IReadOnlyList<KVObject> callInfos;
    private readonly Dictionary<int, Dictionary<int, SocketIn>> instructionInputActionSocketMap = [];
    private readonly List<NodeCallInfo> callNodesToResolve = [];

    struct NodeCallInfo
    {
        public int targetChunk;
        public Node node;
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

    public override void Dispose()
    {
        GLControl?.MouseDoubleClick -= OnMouseDoubleClick;
        base.Dispose();
    }

    protected override void AddUiControls()
    {
        base.AddUiControls();

        GLControl?.MouseDoubleClick += OnMouseDoubleClick;
    }

    // Stringify a KVObject for our purposes
    private static string StringifyKVObject(KVObject obj)
    {
        switch (obj.ValueType)
        {
            case KVValueType.String:
                return $"\"{obj}\"";
            case KVValueType.Boolean:
                return obj.ToBoolean() ? "true" : "false";
            case KVValueType.Array:
                {
                    var list = obj.AsArraySpan();
                    StringBuilder sb = new();
                    sb.Append('[');
                    bool firstElem = true;
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
                return obj.ToString();
        }
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        var screenPoint = new SKPoint(e.Location.X, e.Location.Y);
        var graphPoint = ScreenToGraph(screenPoint);
        var element = nodeGraph.FindElementAt(graphPoint);

        if (element is Node { ExternalResourceName: not null } node)
        {
            var foundFile = VrfGuiContext.FindFileWithContext(node.ExternalResourceName + ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);
            if (foundFile.Context != null)
            {
                Debug.Assert(foundFile.PackageEntry != null);
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
            }
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
        int destInstruction,
        SocketOut outputSocket,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerOutputSocketMap)
    {
        if (destChunk == -1)
            return;

        if (destInstruction < 0)
            destInstruction = 0;

        TraverseNodesForChunk(
            destChunk,
            outputSocket,
            new Dictionary<int, KVObject>(registerConstValueMap),
            new Dictionary<int, SocketOut>(registerOutputSocketMap),
            destInstruction
        );
    }

    private void AddOutflowSocket(
        Node node,
        KVObject outflow,
        string socketLabel,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerOutputSocketMap)
    {
        var destChunk = outflow.GetInt32Property("m_nDestChunk");
        var destInstruction = outflow.GetInt32Property("m_nInstruction");
        if (destChunk == -1 || destInstruction == -1)
            return;
        var outputSocket = node.CreateSocketOut<Flow>(socketLabel);
        TraverseOutflow(destChunk, destInstruction, outputSocket, registerConstValueMap, registerOutputSocketMap);
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

    private static InstructionType GetInstructionType(KVObject instruction)
    {
        string strInstrType = instruction.GetStringProperty("m_nCode");
        if (Enum.TryParse(strInstrType, out InstructionType instrType))
        {
            return instrType;
        }

        return InstructionType.INVALID;
    }

    private PulseValueType GetValueTypeFromRegister(int chunkIdx, int regIdx)
    {
        var chunk = chunks[chunkIdx];
        var regInfo = chunk.GetArray("m_Registers")[regIdx];
        string strRegType = regInfo.GetStringProperty("m_Type");
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

    private KVObject GetConstantValueFromId(int constantId)
    {
        var constant = constants[constantId];
        return constant["m_Value"];
    }

    private KVObject GetDomainValueFromId(int domainValId)
    {
        var constant = domainValues[domainValId];
        return constant["m_Value"];
    }

    private string GetVariableNameFromIndex(int variableIndex)
    {
        var variable = variables[variableIndex];
        return variable.GetStringProperty("m_Name");
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

    private SocketOut CreateSequentialActionSockets(Node node, SocketOut previousActionOutSocket, int chunkIdx, int instruction)
    {
        var socketIn = node.CreateSocketIn<Flow>("");
        nodeGraph.Connect(previousActionOutSocket, socketIn);
        instructionInputActionSocketMap[chunkIdx][instruction] = socketIn;

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
        }
        else
        {
            var obj = registerConstValueMap[regIndex];
            node.AddText($"{name} = {StringifyKVObject(obj)}");
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
            return;

        foreach (var kvPair in inParams)
        {
            var regName = kvPair.Key;
            var regIdx = (int)kvPair.Value;

            if (!registerConstValueMap.TryGetValue(regIdx, out var regValue))
            {
                var regOutSocket = registerSocketOutputMap[regIdx];
                var argInputSocket = node.CreateSocketInFromValueType(regName, GetValueTypeFromRegister(chunkIndex, regIdx));
                nodeGraph.Connect(regOutSocket, argInputSocket);
            }
            else
            {
                node.AddText($"{regName} = {StringifyKVObject(regValue)}");
            }
        }
    }

    private SocketOut? TraverseNodesForChunk(
        int chunkIndex,
        SocketOut sourceActionOutSocket,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerOutputSocketMap,
        int startingInstruction = 0,
        int endingInstruction = int.MaxValue,
        bool ignoreActions = false)
    {
        if (chunkIndex < 0)
            return null;

        instructionInputActionSocketMap.TryAdd(chunkIndex, []);

        var chunk = chunks[chunkIndex];
        var instructions = chunk.GetArray("m_Instructions");
        var registers = chunk.GetArray("m_Registers");

        // Find if we generated the same nodes, and connect incoming socket to the existing nodes.
        // Useful with things like jump instructions that jump directly to an already generated node.
        //if (!forceRecalculateExisting)
        //{
        //    // Skip over potential NOPs if we landed on it through a Jump somehow.
        //    var instrIndex = startingInstruction;
        //    while (instrIndex < instructions.Count && GetInstructionType(instructions[instrIndex]) == InstructionType.NOP)
        //    {
        //        instrIndex++;
        //    }

        //    if (instrIndex < instructions.Count)
        //    {
        //        if (instructionInputActionSocketMap[chunkIndex].TryGetValue(instrIndex, out var targetSocket))
        //        {
        //            nodeGraph.Connect(sourceActionOutSocket, targetSocket);
        //            return null;
        //        }
        //    }
        //}

        var finalEndingInstr = Math.Min(instructions.Count, endingInstruction);
        SocketOut previousActionOutSocket = sourceActionOutSocket;
        bool stopProcessing = false;
        for (var instructionIdx = startingInstruction; instructionIdx < finalEndingInstr; instructionIdx++)
        {
            var instruction = instructions[instructionIdx];
            if (stopProcessing)
                break;

            var instrType = GetInstructionType(instruction);
            var instrNameString = instruction.GetStringProperty("m_nCode");
            switch (instrType)
            {
                case InstructionType.LIBRARY_INVOKE:
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
                                    break;

                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            }
                            // it will be the color of the first output socket
                            node.UpdateTypeColorFromOutput();

                            CreateInputsFromRegisterMap(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, registerMap);

                            nodeGraph.AddNode(node);
                            node = null;
                        }
                        finally
                        {
                            node?.Dispose();
                        }
                        break;
                    }
                case InstructionType.CELL_INVOKE:
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
                                    break;

                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            }
                            // it will be the color of the first output socket
                            node.UpdateTypeColorFromOutput();

                            AddFilteredCellDetails(node, cellIndex);

                            CreateInputsFromRegisterMap(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, registerMap);
                            PopulateSpecificCell(node, cellIndex, registerConstValueMap, registerOutputSocketMap);

                            nodeGraph.AddNode(node);
                            node = null;
                        }
                        finally
                        {
                            node?.Dispose();
                        }
                        break;
                    }
                case InstructionType.GET_CONST:
                    {
                        var constIdx = instruction.GetInt32Property("m_nConstIdx");
                        var outputRegIdx = instruction.GetInt32Property("m_nReg0");
                        registerConstValueMap[outputRegIdx] = GetConstantValueFromId(constIdx);
                        break;
                    }
                case InstructionType.GET_DOMAIN_VALUE:
                    {
                        var domainValIdx = instruction.GetInt32Property("m_nDomainValueIdx");
                        var outputRegIdx = instruction.GetInt32Property("m_nReg0");
                        registerConstValueMap[outputRegIdx] = GetDomainValueFromId(domainValIdx);
                        break;
                    }
                case InstructionType.GET_VAR:
                    {
                        var varIndex = instruction.GetInt32Property("m_nVar");
                        var regIndex = instruction.GetInt32Property("m_nReg0");
                        var node = new Node(null)
                        {
                            Name = "Get Variable",
                            NodeType = "Instruction",
                        };
                        node.AddText(GetVariableNameFromIndex(varIndex));

                        var outSocket = node.CreateSocketOutFromValueType("retval", GetValueTypeFromRegister(chunkIndex, regIndex));
                        registerOutputSocketMap[regIndex] = outSocket;
                        node.UpdateTypeColorFromOutput();

                        nodeGraph.AddNode(node);
                        break;
                    }
                case InstructionType.SET_VAR:
                    {
                        if (ignoreActions)
                            break;

                        var varIndex = instruction.GetInt32Property("m_nVar");
                        var regIndex = instruction.GetInt32Property("m_nReg0");
                        var node = new Node(null)
                        {
                            Name = "Set Variable",
                            NodeType = "Instruction",
                        };
                        previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);

                        node.AddText(GetVariableNameFromIndex(varIndex));
                        AddNodeRegisterInput(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, regIndex, "value");

                        nodeGraph.AddNode(node);
                        break;
                    }
                case InstructionType.PULSE_CALL_SYNC:
                case InstructionType.PULSE_CALL_ASYNC_FIRE:
                    {
                        var callTargetChunk = instruction.GetInt32Property("m_nChunk");
                        var callDestInstruction = instruction.GetInt32Property("m_nDestInstruction");
                        if (callTargetChunk != chunkIndex || callDestInstruction <= 0)
                        {
                            if (ignoreActions)
                                break;

                            var callInfoIndex = instruction.GetInt32Property("m_nCallInfoIndex");
                            var node = new Node(null)
                            {
                                Name = instrType == InstructionType.PULSE_CALL_SYNC ? "Call" : "Call Asynchronously",
                                NodeType = "Flow",
                            };
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            var callInfo = callInfos[callInfoIndex];
                            CreateInputsFromRegisterMap(node, chunkIndex, registerConstValueMap, registerOutputSocketMap, callInfo["m_RegisterMap"]);

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
                                callDestInstruction
                            );

                            if (outSocket != null)
                                previousActionOutSocket = outSocket;
                        }
                        break;
                    }
                case InstructionType.RETURN_VALUE:
                    {
                        if (ignoreActions)
                            break;

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
                            nodeGraph.AddNode(node);
                            node = null;
                        }
                        finally
                        {
                            node?.Dispose();
                        }
                        break;
                    }
                case InstructionType.RETURN_VOID:
                case InstructionType.IMMEDIATE_HALT:
                    {
                        stopProcessing = true;
                        break;
                    }
                case InstructionType.JUMP:
                    {
                        stopProcessing = true;
                        var destInstruction = instruction.GetInt32Property("m_nDestInstruction");
                        TraverseNodesForChunk(
                            chunkIndex,
                            previousActionOutSocket,
                            new Dictionary<int, KVObject>(registerConstValueMap),
                            new Dictionary<int, SocketOut>(registerOutputSocketMap),
                            destInstruction);
                        break;
                    }
                case InstructionType.JUMP_COND:
                    {
                        var reg0 = instruction.GetInt32Property("m_nReg0");
                        // Detect loop
                        var (wasLoop, loopOutSocket) = HandleLoopJump(
                            registers,
                            instructions,
                            registerConstValueMap,
                            registerOutputSocketMap,
                            instructionIdx,
                            chunkIndex,
                            reg0,
                            previousActionOutSocket
                        );
                        if (wasLoop)
                        {
                            previousActionOutSocket = loopOutSocket ?? previousActionOutSocket;
                            stopProcessing = true;
                            break;
                        }

                        // Simple logic branch
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

                            var socketOutTrue = node.CreateSocketOut<Flow>("True");
                            var destInstructionTrue = instruction.GetInt32Property("m_nDestInstruction");
                            TraverseNodesForChunk(
                                chunkIndex,
                                socketOutTrue,
                                new Dictionary<int, KVObject>(registerConstValueMap),
                                new Dictionary<int, SocketOut>(registerOutputSocketMap),
                                destInstructionTrue
                            );

                            // If false we don't take the jump. So traverse starting from currentinstr + 1
                            var socketOutFalse = node.CreateSocketOut<Flow>("False");
                            var destInstructionFalse = instructionIdx + 1;


                            // Find out the jump out instruction after the True case is finished.
                            // Whether the graph code run through true or false, it will end up at one, unless it's just a return
                            // in which case we don't have to worry about anything
                            var firstInsturctionAfterLoopId = -1;
                            if (GetInstructionType(instructions[destInstructionFalse]) == InstructionType.JUMP)
                            {
                                var falseJumpTarget = instructions[destInstructionFalse].GetInt32Property("m_nDestInstruction");

                                if (falseJumpTarget > 0)
                                {
                                    if (GetInstructionType(instructions[falseJumpTarget - 1]) == InstructionType.JUMP)
                                    {
                                        firstInsturctionAfterLoopId = instructions[falseJumpTarget - 1].GetInt32Property("m_nDestInstruction");
                                    }
                                }
                            }

                            TraverseNodesForChunk(chunkIndex,
                                socketOutFalse,
                                new Dictionary<int, KVObject>(registerConstValueMap),
                                new Dictionary<int, SocketOut>(registerOutputSocketMap),
                                destInstructionFalse,
                                firstInsturctionAfterLoopId == -1 ? int.MaxValue : firstInsturctionAfterLoopId
                            );

                            // create even if we're returning, cause the socket still could be connected to further actions
                            // if the current flow was a subroutine
                            previousActionOutSocket = node.CreateSocketOut<Flow>("Finished");
                            if (firstInsturctionAfterLoopId != -1)
                            {
                                instructionIdx = firstInsturctionAfterLoopId;
                            }
                            else
                            {
                                stopProcessing = true;
                            }

                            if (!ignoreActions)
                            {
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
                                continue;

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

                            // create output socket for this node, and store it for future connections
                            var socketOut = node.CreateSocketOutFromValueType("retval", GetValueTypeFromRegister(chunkIndex, reg0));
                            registerOutputSocketMap[reg0] = socketOut;
                            node.UpdateTypeColorFromOutput();

                            nodeGraph.AddNode(node);
                        }

                        break;
                    }

            }
        }
        return previousActionOutSocket;
    }

    private void GeneratePossibleOutflowsForCell(Node node, int cellIdx, Dictionary<int, KVObject> registerConstValueMap, Dictionary<int, SocketOut> registerOutputSocketMap)
    {
        var cell = cells[cellIdx];
        foreach (var outflow in cell.Values)
        {
            if (outflow.TryGetValue("m_SourceOutflowName", out var outflowName))
            {
                AddOutflowSocket(node, outflow, outflowName.ToString(), registerConstValueMap, registerOutputSocketMap);
            }
        }
    }

    // Retrives m_nFlowNodeID from m_InstructionDebugInfos for a particular instruction
    // For older files retrieve the ID from m_InstructionEditorIDs
    private int GetInstructionFlowId(int chunkId, int instructionId)
    {
        var chunk = chunks[chunkId];
        if (chunk.TryGetValue("m_InstructionDebugInfos", out var debugInfos))
        {
            return debugInfos.AsArraySpan()[instructionId].GetInt32Property("m_nFlowNodeID");
        }

        if (chunk.TryGetValue("m_InstructionEditorIDs", out var editorIds))
        {
            return editorIds.AsArraySpan()[instructionId].ToInt32();
        }

        throw new Exception("No 'm_InstructionDebugInfos', or 'm_InstructionEditorIDs' are present in a chunk for this graph definition. Graph schema updated?");
    }

    private (bool, SocketOut?) HandleLoopJump(
        IReadOnlyList<KVObject> registers,
        IReadOnlyList<KVObject> instructions,
        Dictionary<int, KVObject> registerConstValueMap,
        Dictionary<int, SocketOut> registerOutputSocketMap,
        int instructionIdx,
        int chunkIndex,
        int conditionalRegister,
        SocketOut lastActionSocket)
    {
        var regInfo = registers[conditionalRegister];
        var originName = regInfo.GetStringProperty("m_OriginName");
        if (!originName.EndsWith("__loop_cond", StringComparison.InvariantCulture))
        {
            return (false, null);
        }

        var relevantNodeNumber = originName.Split(':')[0];
        var instrComp = instructions[regInfo.GetInt32Property("m_nWrittenByInstruction")];
        // assuming a 'for' loop, one register is going to be the index (can find out through originName)
        // the other one will be the max/min value
        // Also they will have the same node-id specified in OriginName that we can also check to verify
        IReadOnlyList<int> regs = [instrComp.GetInt32Property("m_nReg1"), instrComp.GetInt32Property("m_nReg2")];
        int regLoopIndex = -1;
        int regIndexStop = -1;
        foreach (var regIdx in regs)
        {
            var regData = registers[regIdx];
            var originNameLoop = regData.GetStringProperty("m_OriginName");
            if (originNameLoop.EndsWith("__loop_index", StringComparison.InvariantCulture))
            {
                regLoopIndex = regIdx;
            }
            else
            {
                regIndexStop = regIdx;
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
            if (regLoopIndex != -1 && regIndexStop != -1)
            {
                var loopSocketIn = forLoopNode.CreateSocketIn<Flow>("");
                nodeGraph.Connect(lastActionSocket, loopSocketIn);
                instructionInputActionSocketMap[chunkIndex][instructionIdx] = loopSocketIn;

                AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, regLoopIndex, "First index");
                AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, regIndexStop, "Last index");
                AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, conditionalRegister, "Loop condition");

                // add the index output
                // this will be remembered when we do a loop iteration (should also handle foreach type of loop)
                registerOutputSocketMap[regLoopIndex] = forLoopNode.CreateSocketOut<ValueNumber>("Index");

                // Usually a uncoditional jump follows after a conditional - this one jumps outside the loop
                // Instruction after this jump should be the first loop instruction

                var loopJumpOutInstructionId = instructionIdx + 1;
                var loopJumpOutInstruction = instructions[loopJumpOutInstructionId];
                if (GetInstructionType(loopJumpOutInstruction) != InstructionType.JUMP)
                {
                    VrfGuiContext.Logger.LogWarning("PulseGraph: Unhandled 'for' loop setup in graph!");
                    return (false, null);
                }
                // figure out where the loop ends
                var loopEndInstruction = loopJumpOutInstruction.GetInt32Property("m_nDestInstruction");
                int loopOperationEndInstructionId = -1;
                // see if we can find the increment, which should be added at the end of an iteration


                // find a jump that jumps to the same or earlier instruction id of this conditional jump that matches the ID
                // then we know that it's a part of this loop. That's how we find where the loop ends
                var instructionIdLoopJumpBack = -1;
                var nodeId = GetInstructionFlowId(chunkIndex, instructionIdx);
                for (var j = instructionIdx + 1; j < instructions.Count; j++)
                {
                    var instr = instructions[j];
                    if (GetInstructionType(instr) != InstructionType.JUMP)
                    {
                        continue;
                    }

                    if (instr.GetInt32Property("m_nDestInstruction") <= instructionIdx && GetInstructionFlowId(chunkIndex, j) == nodeId)
                    {
                        instructionIdLoopJumpBack = j;
                        break;
                    }
                }

                int regIncrement = -1;
                for (var regIdx = 0; regIdx < registers.Count; regIdx++)
                {
                    var regData = registers[regIdx];
                    var originNameIncrement = regData.GetStringProperty("m_OriginName");
                    if (!originNameIncrement.StartsWith(nodeId.ToString()))
                    {
                        continue;
                    }

                    if (regData.GetStringProperty("m_OriginName").EndsWith("__increment", StringComparison.InvariantCulture))
                    {
                        regIncrement = regIdx;

                        // ASSUME that we got this increment from a constant (previous instruction)
                        // TODO: actually follow the register chain until we get a const, then connect these operations to the increment socket
                        var constIcrement = GetConstantValueFromId(instructions[instructionIdLoopJumpBack - 2].GetInt32Property("m_nConstIdx"));
                        forLoopNode.AddText($"Increment = {StringifyKVObject(constIcrement)}");
                        loopOperationEndInstructionId = instructionIdLoopJumpBack - 2;
                        break;
                    }
                }

                if (regIncrement == -1)
                {
                    VrfGuiContext.Logger.LogInformation("PulseGraph: 'for' loop expected __increment register, but didn't find a matching one.");
                }

                if (loopOperationEndInstructionId == loopJumpOutInstructionId)
                {
                    VrfGuiContext.Logger.LogInformation("PulseGraph: Empty 'for' loop!");
                    return (false, null);
                }

                var socketOutLoopAction = forLoopNode.CreateSocketOut<Flow>("Loop");
                TraverseNodesForChunk(
                    chunkIndex,
                    socketOutLoopAction,
                    new Dictionary<int, KVObject>(registerConstValueMap),
                    new Dictionary<int, SocketOut>(registerOutputSocketMap),
                    loopJumpOutInstructionId + 1,
                    loopOperationEndInstructionId
                );

                var socketOutFinishedAction = forLoopNode.CreateSocketOut<Flow>("Finished");
                var lastActSocket = TraverseNodesForChunk(
                    chunkIndex,
                    socketOutFinishedAction,
                    new Dictionary<int, KVObject>(registerConstValueMap),
                    new Dictionary<int, SocketOut>(registerOutputSocketMap),
                    loopEndInstruction
                );

                nodeGraph.AddNode(forLoopNode);
                forLoopNode = null;

                return (true, lastActSocket);
            }
            // different type of loop ?
            // find names with m_Start, m_Stop, m_Step with the same nodeid of jump_cond condition register
            else
            {
                var regStop = -1;
                var regStep = -1;
                var regStart = -1;

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
                    }
                    else if (originNameCurr.EndsWith("m_Start", StringComparison.InvariantCulture))
                    {
                        regStart = reg.GetInt32Property("m_nReg");
                    }
                }

                if (regStop == -1 || regStep == -1 || regStart == -1)
                {
                    return (false, null);
                }

                var loopSocketIn = forLoopNode.CreateSocketIn<Flow>("");
                nodeGraph.Connect(lastActionSocket, loopSocketIn);
                instructionInputActionSocketMap[chunkIndex][instructionIdx] = loopSocketIn;

                AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, regStart, "First index");
                AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, regStop, "Last index");
                AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, regStep, "Increment");
                AddNodeRegisterInput(forLoopNode, chunkIndex, registerConstValueMap, registerOutputSocketMap, conditionalRegister, "Loop condition");

                registerOutputSocketMap[regLoopIndex] = forLoopNode.CreateSocketOut<ValueNumber>("Index");

                var loopJumpOutInstructionId = instructionIdx + 1;
                var loopJumpOutInstruction = instructions[loopJumpOutInstructionId];
                if (GetInstructionType(loopJumpOutInstruction) != InstructionType.JUMP)
                {
                    VrfGuiContext.Logger.LogWarning("PulseGraph: Unhandled 'for' loop setup in graph!");
                    return (false, null);
                }

                // find a jump that jumps to the same or earlier instruction id of this conditional jump that matches the ID
                // then we know that it's a part of this loop. That's how we find where the loop ends
                var instructionIdLoopJumpBack = -1;
                var nodeId = GetInstructionFlowId(chunkIndex, instructionIdx);
                for (var j = instructionIdx + 1; j < instructions.Count; j++)
                {
                    var instr = instructions[j];
                    if (GetInstructionType(instr) != InstructionType.JUMP)
                    {
                        continue;
                    }

                    if (instr.GetInt32Property("m_nDestInstruction") <= instructionIdx && GetInstructionFlowId(chunkIndex, j) == nodeId)
                    {
                        instructionIdLoopJumpBack = j;
                        break;
                    }
                }

                var instrLoopEndingId = instructionIdLoopJumpBack - 1;
                var loopEndInstruction = instructionIdLoopJumpBack + 1;

                var socketOutLoopAction = forLoopNode.CreateSocketOut<Flow>("Loop");
                TraverseNodesForChunk(
                    chunkIndex,
                    socketOutLoopAction,
                    new Dictionary<int, KVObject>(registerConstValueMap),
                    new Dictionary<int, SocketOut>(registerOutputSocketMap),
                    loopJumpOutInstructionId + 1,
                    instrLoopEndingId
                );

                var socketOutFinishedAction = forLoopNode.CreateSocketOut<Flow>("Finished");
                var lastActSocket = TraverseNodesForChunk(
                    chunkIndex,
                    socketOutFinishedAction,
                    new Dictionary<int, KVObject>(registerConstValueMap),
                    new Dictionary<int, SocketOut>(registerOutputSocketMap),
                    loopEndInstruction
                );

                nodeGraph.AddNode(forLoopNode);
                forLoopNode = null;

                return (true, lastActSocket);
            }
        }
        finally
        {
            forLoopNode?.Dispose();
        }
    }
    private void PopulateSpecificCell(Node node, int cellIdx, Dictionary<int, KVObject> registerConstValueMap, Dictionary<int, SocketOut> registerOutputSocketMap)
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
                        var destInstr = wakeResume.GetInt32Property("m_nInstruction");

                        var outputSocket = node.CreateSocketOut<Flow>("OnFinished");
                        TraverseOutflow(destChunk, destInstr, outputSocket, registerConstValueMap, registerOutputSocketMap);
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
                                    AddOutflowSocket(node, output, outflowName, registerConstValueMap, registerOutputSocketMap);
                                }
                                break;
                            }

                    }
                    GeneratePossibleOutflowsForCell(node, cellIdx, registerConstValueMap, registerOutputSocketMap);

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
                                    break;

                                var publicOutput = publicOutputs[outputIndex];
                                var outputName = publicOutput.GetStringProperty("m_Name");
                                var outputDesc = publicOutput.GetStringProperty("m_Description");

                                node.AddText($"Public Output: {outputName}");
                                node.AddText(outputDesc);
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
                                        continue;

                                    var outflowName = eventOutflow.GetStringProperty("m_SourceOutflowName");

                                    var timeFromPrevious = timelineEvent.GetFloatProperty("m_flTimeFromPrevious");
                                    var socketLabel = $"(Time from prev: {timeFromPrevious}s) | {outflowName}";
                                    AddOutflowSocket(node, eventOutflow, socketLabel, registerConstValueMap, registerOutputSocketMap);
                                }
                                break;
                            }
                    }
                    // Some cells don't have Outflow in the name, but still have outflows.
                    GeneratePossibleOutflowsForCell(node, cellIdx, registerConstValueMap, registerOutputSocketMap);
                    break;
                }
        }
    }

    private void CreateGraph()
    {
        Dictionary<int, string> chunkFunctionName = [];
        int currentUnknownNamedFuncNumber = 0;

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
                                continue;

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
                                new Dictionary<int, KVObject>(),
                                registerSocketOutputMap
                            );
                            chunkFunctionName.Add(entryChunkIdx, cells[cellIdx].GetStringProperty("m_MethodName"));

                            AddFilteredCellDetails(cellNode, cellIdx);

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
            for (int chunkId = 0; chunkId < chunks.Count; chunkId++)
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

                    TraverseNodesForChunk(chunkId, outputSocket, new Dictionary<int, KVObject>(), new Dictionary<int, SocketOut>());
                    nodeGraph.AddNode(cellNode);
                }
            }
        }

        // General info as a node (probably temporary until UI pane is added)
        var graphInfoNode = new Node(null)
        {
            Name = "Graph info",
            NodeType = "Imagine that I'm a static panel",
        };
        if (graphDefinition.ContainsKey("m_DomainIdentifier"))
        {
            graphInfoNode.AddText($"Domain: {graphDefinition.GetStringProperty("m_DomainIdentifier")}");
        }
        if (graphDefinition.ContainsKey("m_DomainSubType"))
        {
            graphInfoNode.AddText($"Domain sub-type: {graphDefinition.GetStringProperty("m_DomainSubType")}");
        }
        if (graphDefinition.ContainsKey("m_ParentMapName"))
        {
            graphInfoNode.AddText($"Description: {graphDefinition.GetStringProperty("m_ParentMapName")}");
        }
        if (graphDefinition.ContainsKey("m_ParentXmlName"))
        {
            graphInfoNode.AddText($"Parent XML panel: {graphDefinition.GetStringProperty("m_ParentXmlName")}");
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

            string description = variable.GetStringProperty("m_Description");
            if (!string.IsNullOrEmpty(description))
            {
                node.AddText(description);
            }
            nodeGraph.AddNode(node);
        }

        // Resolve call nodes to display the target function name
        foreach (var callNodeInfo in callNodesToResolve)
        {
            var targetChunk = callNodeInfo.targetChunk;
            string methodNameToCall = chunkFunctionName[targetChunk];
            callNodeInfo.node.AddText($"Method: {methodNameToCall}");
            nodeGraph.AddNode(callNodeInfo.node);
        }

        nodeGraph.LayoutNodes();
    }
    #region Nodes
    class Node : AbstractNode
    {
        public KVObject? Data { get; set; }
        public string? ExternalResourceName { get; set; }
        public Node(KVObject? data)
        {
            Data = data;
            BaseColor = NodeColor;
            TextColor = NodeTextColor;
            HeaderColor = ToSKColor(ControlPaint.Light(Color.FromArgb(NodeColor.Red, NodeColor.Green, NodeColor.Blue)));
            HeaderTextColor = new SKColor(5, 5, 5);
            HeaderTypeColor = new SKColor(25, 25, 25);
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

            if (string.IsNullOrEmpty(ExternalResourceName))
            {
                return;
            }

            // Get icon from cache
            IconCache.TryGetValue("pulse", out var iconToUse);

            const int iconSize = 16;

            var yOffset = Sockets.Count > 1 ? 55 : 45;
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
                Y = position.Y + ArialFont.Size + 1
            };

            var fileExtensionStart = ExternalResourceName.LastIndexOf('.');
            var trimStr = ExternalResourceName[..fileExtensionStart];
            trimStr = trimStr.Replace(".vpulse", string.Empty, StringComparison.Ordinal);
            var lastSlashIndex = trimStr.LastIndexOf('/');
            if (lastSlashIndex >= 0)
            {
                trimStr = trimStr[(lastSlashIndex + 1)..];
            }
            if (trimStr.Length > 23)
            {
                trimStr = '…' + trimStr[^22..];
            }

            using var paint = new SKPaint { Color = new(255, 0, 0), IsAntialias = true };
            canvas.DrawText(trimStr, textPosition.X, textPosition.Y, ArialFont, paint);
        }

        private static readonly Dictionary<string, SKSvg> IconCache = [];

        static Node()
        {
            string[] icons =
            [
                "pulse",
            ];

            foreach (var iconName in icons)
            {
                using var svgResource = Program.Assembly.GetManifestResourceStream($"GUI.Icons.AssetTypes.{iconName}.svg");
                Debug.Assert(svgResource is not null);

                var svg = new SKSvg();
                svg.Load(svgResource);
                IconCache[iconName] = svg;
            }
        }
    }

    #endregion Nodes

}
