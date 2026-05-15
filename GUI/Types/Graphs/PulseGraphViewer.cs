using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using HarfBuzzSharp;
using NAudio.Utils;
using SkiaSharp;
using Svg.Skia;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;
using Windows.ApplicationModel.Resources.Management;
using static ValveResourceFormat.Blocks.ResourceIntrospectionManifest.ResourceDiskEnum;

namespace GUI.Types.Graphs;

using InstructionInputActionSocketMap = Dictionary<int, SocketIn>;
using RegisterSocketOutputMap = Dictionary<int, SocketOut>;
using RegisterValueMap = Dictionary<int, KVObject>;
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

    struct NodeCallInfo
    {
        public int targetChunk;
        public Node node;
    }

    private static SKColor ActionColor { get; set; }
    private struct Action;

    private static SKColor ValueColor { get; set; }
    private struct Value;

    public PulseGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, CreateAndConfigureNodeGraph(data, out var graphDef))
    {
        graphDefinition = graphDef;

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
                return $"\"{obj.ToString()}\"";
            case KVValueType.Boolean:
                return obj.ToBoolean() ? "true" : "false";
            case KVValueType.Int16:
            case KVValueType.UInt16:
            case KVValueType.Int32:
            case KVValueType.UInt32:
            case KVValueType.Int64:
            case KVValueType.UInt64:
            case KVValueType.FloatingPoint:
            case KVValueType.FloatingPoint64:
                return obj.ToString();
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

    private static NodeGraphControl CreateAndConfigureNodeGraph(KVObject data, out KVObject graphDef)
    {
        graphDef = data;
        var nodeGraph = new NodeGraphControl
        {
            GridStyle = NodeGraphControl.EGridStyle.Grid,

            CanvasBackgroundColor = new SKColor(40, 40, 40)
        };
        NodeColor = new SKColor(63, 95, 107);
        NodeTextColor = new SKColor(230, 230, 230);
        nodeGraph.GridColor = SKColors.White;

        ValueColor = new SKColor(26, 164, 214);
        ActionColor = new SKColor(217, 20, 207);

        if (Themer.CurrentTheme == Themer.AppTheme.Dark)
        {
            nodeGraph.CanvasBackgroundColor = ToSKColor(Themer.CurrentThemeColors.AppMiddle);
            NodeColor = ToSKColor(Themer.CurrentThemeColors.AppSoft);
            nodeGraph.GridColor = ToSKColor(Themer.CurrentThemeColors.ContrastSoft);
            //PoseColor = ToSKColor(ControlPaint.Dark(Color.LightGreen, 0f));
            //ValueColor = ToSKColor(ControlPaint.Dark(Color.LightBlue, 0f));
        }

        NodeGraphControl.AddTypeColorPair<Value>(ValueColor);
        NodeGraphControl.AddTypeColorPair<Action>(ActionColor);

        return nodeGraph;
    }

    private void CreateGraph()
    {
        var cells = graphDefinition.GetArray("m_Cells");
        var chunks = graphDefinition.GetArray("m_Chunks");
        var invokeBindings = graphDefinition.GetArray("m_InvokeBindings");
        var domainValues = graphDefinition.GetArray("m_DomainValues");
        var constants = graphDefinition.GetArray("m_Constants");
        var variables = graphDefinition.GetArray("m_Vars");
        var publicOutputs = graphDefinition.GetArray("m_PublicOutputs");
        var callInfos = graphDefinition.GetArray("m_CallInfos");
        // Registers that have known loaded constant value.
        Dictionary<int, RegisterValueMap> staticCalculatedRegisterValues = [];
        // Cache registers to output sockets that are calculated by a node.
        Dictionary<int, RegisterSocketOutputMap> registerSocketOutputMap = [];
        // Cache existing instructions that can be referenced back by things like 'jump' instructions.
        Dictionary<int, InstructionInputActionSocketMap> instructionInputActionSocketMap = [];
        Dictionary<int, HashSet<int>> instructionShouldSkipOverWhenEvaluatingRecursively = [];
        // Function name tied to the chunk.
        Dictionary<int, string> chunkFunctionName = [];
        int currentUnknownNamedFuncNumber = 0;

        Dictionary<int, Node> createdNodes = [];
        List<NodeCallInfo> callNodesToResolve = [];

        CellCategory GetCellCategory(int cellIdx)
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

        InstructionType GetInstructionType(KVObject instruction)
        {
            string strInstrType = instruction.GetStringProperty("m_nCode");
            if (Enum.TryParse(strInstrType, out InstructionType instrType))
            {
                return instrType;
            }

            return InstructionType.INVALID;
        }

        CellType GetCellType(int cellIdx, out string cellTypeString)
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

        KVObject GetConstantValueFromId(int constantId)
        {
            var constant = constants[constantId];
            return constant["m_Value"];
        }

        KVObject GetDomainValueFromId(int domainValId)
        {
            var constant = domainValues[domainValId];
            return constant["m_Value"];
        }

        string GetVariableNameFromIndex(int variableIndex)
        {
            var variable = variables[variableIndex];
            return variable.GetStringProperty("m_Name");
        }

        // Filter out some internal fields, keep only what's derived from the base cell class and useful for display
        KVObject FilterBaseCellFieldsForDisplay(int cellIndex)
        {
            string[] filterKeys = ["_class", "m_EntryChunk", "m_nEditorNodeID"];
            var cell = cells[cellIndex];
            var filteredCell = new KVObject();

            foreach (var key in cell.Keys)
            {
                if (!filterKeys.Contains(key) && !cell[key].IsCollection && !cell[key].IsArray) // we probably don't want to display collections
                {
                    filteredCell[key] = cell[key];
                }
            }
            return filteredCell;
        }

        SocketOut CreateSequentialActionSockets(Node node, SocketOut previousActionOutSocket, int chunkIdx, int instruction)
        {
            var socketIn = node.CreateSocketIn<Action>("");
            nodeGraph.Connect(previousActionOutSocket, socketIn);
            instructionInputActionSocketMap[chunkIdx][instruction] = socketIn;

            var socketOut = new SocketOut(typeof(Action), "", node);
            node.Sockets.Add(socketOut);
            return socketOut;
        }

        void AddNodeRegisterInput(Node node, int chunkIndex, Dictionary<int, SocketOut> registerSocketOutputMap, int regIndex)
        {
            if (staticCalculatedRegisterValues[chunkIndex].TryGetValue(regIndex, out var regValue))
            {
                node.AddText(StringifyKVObject(regValue));
            }
            else
            {
                var regOutSocket = registerSocketOutputMap[regIndex];
                var argInputSocket = node.CreateSocketIn<Value>("value");
                nodeGraph.Connect(regOutSocket, argInputSocket);
            }
        }

        void CreateInputsFromRegisterMap(Node node, int chunkIndex, Dictionary<int, SocketOut> registerSocketOutputMap, KVObject registerMap)
        {
            var inParams = registerMap["m_Inparams"];
            if (inParams.IsNull)
                return;

            foreach (var kvPair in inParams)
            {
                var regName = kvPair.Key;
                var regIdx = (int)kvPair.Value;

                if (!staticCalculatedRegisterValues.TryGetValue(chunkIndex, out var chunkRegMap))
                    continue;

                if (!chunkRegMap.TryGetValue(regIdx, out var regValue))
                {
                    var regOutSocket = registerSocketOutputMap[regIdx];
                    var argInputSocket = node.CreateSocketIn<Value>(regName);
                    nodeGraph.Connect(regOutSocket, argInputSocket);
                }
                else
                {
                    node.AddText($"{regName} = {StringifyKVObject(regValue)}");
                }
            }
        }

        /// <summary>
        /// Populates relevant information and creates node graphs for the given chunk
        /// Will loop over a chunk until hitting a return instruction. Jumps are processed recursively.
        /// </summary>
        /// <param name="chunkIndex">Index of the chunk to traverse</param>
        /// <param name="sourceActionOutSocket">Which socket to connect the action input of the first generated node</param>
        /// <param name="startingInstruction">Should we skip parsing these amount of instructions? Useful for jumps</param>
        /// <param name="forceRecalculateExisting">Should we generate new nodes instead of plugging in into already generated ones (if they exist)</param>
        void TraverseNodesForChunk(int chunkIndex, SocketOut sourceActionOutSocket, int startingInstruction = 0, bool forceRecalculateExisting = false)
        {
            if (chunkIndex < 0)
                return;

            staticCalculatedRegisterValues.TryAdd(chunkIndex, []);
            registerSocketOutputMap.TryAdd(chunkIndex, []);
            instructionInputActionSocketMap.TryAdd(chunkIndex, []);
            instructionShouldSkipOverWhenEvaluatingRecursively.TryAdd(chunkIndex, []);

            var chunk = chunks[chunkIndex];
            var instructions = chunk.GetArray("m_Instructions");
            var registers = chunk.GetArray("m_Registers");

            // Find if we generated the same nodes, and connect incoming socket to the existing nodes.
            // Useful with things like jump instructions.
            if (!forceRecalculateExisting)
            {
                // Skip over potential NOPs if we landed on it through a Jump somehow.
                var instrIndex = startingInstruction;
                while (instrIndex < instructions.Count && instructionShouldSkipOverWhenEvaluatingRecursively[chunkIndex].Contains(instrIndex))
                {
                    instrIndex++;
                }

                if (instrIndex < instructions.Count)
                {
                    if (instructionInputActionSocketMap[chunkIndex].TryGetValue(instrIndex, out var targetSocket))
                    {
                        nodeGraph.Connect(sourceActionOutSocket, targetSocket);
                        return;
                    }
                }
            }

            SocketOut previousActionOutSocket = sourceActionOutSocket;
            bool stopProcessing = false;
            foreach (var (_instructionIdx, instruction) in instructions.Skip(startingInstruction).Select((index, item) => (item, index)))
            {
                // cause we're skipping elements...
                var instructionIdx = _instructionIdx + startingInstruction;

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
                            var node = new Node(null)
                            {
                                Name = funcName,
                                NodeType = "Function",
                            };

                            // If an action without outputs then create a new output action and continue.
                            if (registerMap["m_Outparams"].IsNull)
                            {
                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            }
                            else // In other case cache the register output values and create output sockets.
                            {
                                var outParams = registerMap["m_Outparams"];
                                foreach (var (paramName, regIdx) in outParams)
                                {
                                    var regValue = (int)regIdx;

                                    var outSocket = new SocketOut(typeof(Value), paramName, node);
                                    registerSocketOutputMap[chunkIndex][regValue] = outSocket;
                                    node.Sockets.Add(outSocket);
                                }
                            }

                            CreateInputsFromRegisterMap(node, chunkIndex, registerSocketOutputMap[chunkIndex], registerMap);

                            nodeGraph.AddNode(node);
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
                            var node = new Node(null)
                            {
                                Name = cellName,
                                // show name after '::' separator, if can't find then show full name
                                NodeType = funcName[(funcNameSplitIdx >= 0 ? (funcNameSplitIdx + 2) : 0)..],
                            };

                            // If an action without outputs then create a new output action and continue.
                            if (registerMap["m_Outparams"].IsNull)
                            {
                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            }
                            else // In other case cache the register output values and create output sockets.
                            {
                                var outParams = registerMap["m_Outparams"];
                                foreach (var (paramName, regIdx) in outParams)
                                {
                                    var regValue = (int)regIdx;

                                    var outSocket = new SocketOut(typeof(Value), paramName, node);
                                    registerSocketOutputMap[chunkIndex][regValue] = outSocket;
                                    node.Sockets.Add(outSocket);
                                }
                                instructionShouldSkipOverWhenEvaluatingRecursively[chunkIndex].Add(instructionIdx);
                            }

                            var morethingies = FilterBaseCellFieldsForDisplay(cellIndex);
                            foreach (var kvPair in morethingies)
                            {
                                node.AddText($"{kvPair.Key} = {StringifyKVObject(kvPair.Value)}");
                            }

                            CreateInputsFromRegisterMap(node, chunkIndex, registerSocketOutputMap[chunkIndex], registerMap);
                            PopulateSpecificCell(node, cellIndex);

                            nodeGraph.AddNode(node);
                            break;
                        }
                    case InstructionType.GET_CONST:
                        {
                            var constIdx = instruction.GetInt32Property("m_nConstIdx");
                            var outputRegIdx = instruction.GetInt32Property("m_nReg0");
                            staticCalculatedRegisterValues[chunkIndex][outputRegIdx] = GetConstantValueFromId(constIdx);
                            instructionShouldSkipOverWhenEvaluatingRecursively[chunkIndex].Add(instructionIdx);
                            break;
                        }
                    case InstructionType.GET_DOMAIN_VALUE:
                        {
                            var domainValIdx = instruction.GetInt32Property("m_nDomainValueIdx");
                            var outputRegIdx = instruction.GetInt32Property("m_nReg0");
                            staticCalculatedRegisterValues[chunkIndex][outputRegIdx] = GetDomainValueFromId(domainValIdx);
                            instructionShouldSkipOverWhenEvaluatingRecursively[chunkIndex].Add(instructionIdx);
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

                            var sockOut = new SocketOut(typeof(Value), "retval", node);
                            registerSocketOutputMap[chunkIndex][regIndex] = sockOut;
                            node.Sockets.Add(sockOut);

                            nodeGraph.AddNode(node);
                            instructionShouldSkipOverWhenEvaluatingRecursively[chunkIndex].Add(instructionIdx);
                            break;
                        }
                    case InstructionType.SET_VAR:
                        {
                            var varIndex = instruction.GetInt32Property("m_nVar");
                            var regIndex = instruction.GetInt32Property("m_nReg0");
                            var node = new Node(null)
                            {
                                Name = "Set Variable",
                                NodeType = "Instruction",
                            };
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);

                            node.AddText(GetVariableNameFromIndex(varIndex));
                            AddNodeRegisterInput(node, chunkIndex, registerSocketOutputMap[chunkIndex], regIndex);

                            nodeGraph.AddNode(node);
                            break;
                        }
                    case InstructionType.PULSE_CALL_SYNC:
                    case InstructionType.PULSE_CALL_ASYNC_FIRE:
                        {
                            var callTargetChunk = instruction.GetInt32Property("m_nChunk");
                            var callInfoIndex = instruction.GetInt32Property("m_nCallInfoIndex");
                            var node = new Node(null)
                            {
                                Name = instrType == InstructionType.PULSE_CALL_SYNC ? "Call" : "Call Asynchronously",
                                NodeType = "Flow",
                            };
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            var callInfo = callInfos[callInfoIndex];
                            CreateInputsFromRegisterMap(node, chunkIndex, registerSocketOutputMap[chunkIndex], callInfo["m_RegisterMap"]);

                            callNodesToResolve.Add(new NodeCallInfo
                            {
                                targetChunk = callTargetChunk,
                                node = node,
                            });
                            break;
                        }
                    case InstructionType.RETURN_VALUE:
                        {
                            var regIndex = instruction.GetInt32Property("m_nReg0");
                            var node = new Node(null)
                            {
                                Name = "Return Value",
                                NodeType = "Flow",
                            };
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket, chunkIndex, instructionIdx);
                            AddNodeRegisterInput(node, chunkIndex, registerSocketOutputMap[chunkIndex], regIndex);
                            break;
                        }
                    case InstructionType.RETURN_VOID:
                    case InstructionType.IMMEDIATE_HALT:
                        {
                            stopProcessing = true;
                            instructionShouldSkipOverWhenEvaluatingRecursively[chunkIndex].Add(instructionIdx);
                            break;
                        }
                    case InstructionType.JUMP:
                        {
                            stopProcessing = true;
                            var destInstruction = instruction.GetInt32Property("m_nDestInstruction");
                            TraverseNodesForChunk(chunkIndex, previousActionOutSocket, destInstruction);
                            instructionShouldSkipOverWhenEvaluatingRecursively[chunkIndex].Add(instructionIdx);
                            break;
                        }
                    case InstructionType.JUMP_COND:
                        {
                            stopProcessing = true;
                            var node = new Node(null)
                            {
                                Name = "If",
                                NodeType = "Flow control",
                            };

                            var socketIn = node.CreateSocketIn<Action>("");
                            nodeGraph.Connect(previousActionOutSocket, socketIn);
                            instructionInputActionSocketMap[chunkIndex][instructionIdx] = socketIn;

                            var reg0 = instruction.GetInt32Property("m_nReg0");
                            if (reg0 != -1)
                            {
                                bool reg0Calculated = false;
                                // Static value exists
                                if (staticCalculatedRegisterValues[chunkIndex].TryGetValue(reg0, out var reg1Val))
                                {
                                    reg0Calculated = true;
                                    node.AddText($"Condition = {StringifyKVObject(reg1Val)}");
                                }

                                if (!reg0Calculated)
                                {
                                    // if it doesn't exist then instruction registers were defined out of order, or we didn't calculate one.
                                    // TODO: handle this case wtihout crashing.
                                    var regOutSocket = registerSocketOutputMap[chunkIndex][reg0];
                                    var argInputSocket = node.CreateSocketIn<Value>("Condition");
                                    nodeGraph.Connect(regOutSocket, argInputSocket);
                                }
                            }

                            var socketOutTrue = new SocketOut(typeof(Action), "True", node);
                            var destInstructionTrue = instruction.GetInt32Property("m_nDestInstruction");
                            TraverseNodesForChunk(chunkIndex, socketOutTrue, destInstructionTrue);
                            node.Sockets.Add(socketOutTrue);

                            // If false we don't take the jump. So traverse starting from currentinstr + 1
                            var socketOutFalse = new SocketOut(typeof(Action), "False", node);
                            var destInstructionFalse = instructionIdx + 1;
                            TraverseNodesForChunk(chunkIndex, socketOutFalse, destInstructionFalse);
                            node.Sockets.Add(socketOutFalse);

                            nodeGraph.AddNode(node);
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

                                bool reg1Calculated = false;
                                bool reg2Calculated = false;

                                if (reg1 != -1)
                                {
                                    // Static value exists
                                    if (staticCalculatedRegisterValues[chunkIndex].TryGetValue(reg1, out var reg1Val))
                                    {
                                        reg1Calculated = true;
                                        node.AddText($"arg1 = {StringifyKVObject(reg1Val)}");
                                    }

                                    if (!reg1Calculated)
                                    {
                                        // if it doesn't exist then instruction registers were defined out of order, or we didn't calculate one.
                                        // TODO: handle this case wtihout crashing.
                                        var regOutSocket = registerSocketOutputMap[chunkIndex][reg1];
                                        var argInputSocket = node.CreateSocketIn<Value>("arg1");
                                        nodeGraph.Connect(regOutSocket, argInputSocket);
                                    }
                                }

                                if (reg2 != -1)
                                {
                                    if (staticCalculatedRegisterValues[chunkIndex].TryGetValue(reg2, out var reg2Val))
                                    {
                                        reg2Calculated = true;
                                        node.AddText($"arg2 = {StringifyKVObject(reg2Val)}");
                                    }

                                    if (!reg2Calculated)
                                    {
                                        // if it doesn't exist then instruction registers were defined out of order, or we didn't calculate one.
                                        // TODO: handle this case wtihout crashing.
                                        var regOutSocket = registerSocketOutputMap[chunkIndex][reg2];
                                        var argInputSocket = node.CreateSocketIn<Value>("arg2");
                                        nodeGraph.Connect(regOutSocket, argInputSocket);
                                    }
                                }

                                // create output socket for this node, and store it for future connections
                                var socketOut = new SocketOut(typeof(Value), "retval", node);
                                node.Sockets.Add(socketOut);
                                registerSocketOutputMap[chunkIndex][reg0] = socketOut;

                                nodeGraph.AddNode(node);
                            }

                            instructionShouldSkipOverWhenEvaluatingRecursively[chunkIndex].Add(instructionIdx);
                            break;
                        }

                }
            }
        }

        // Handle some named outflows,
        // TODO: don't hardcode, instead check for the existence of cell keys to see if it's a CPulse_ResumePoint
        void HandleGenericOutflowsForCell(Node node, int cellIdx)
        {
            var cell = cells[cellIdx];
            List<string> baseOutflowNames = [
                "m_OnFired",
                "m_OnCanceled",
                "m_OnFinished",
                "m_WaitComplete",
                "m_Condition",
                "m_OnTrue",
                "m_Completed",
                "m_OnInterval"
            ];

            foreach (var outflowName in baseOutflowNames)
            {
                if (cell.TryGetValue(outflowName, out var outflow))
                {
                    var destChunk = outflow.GetInt32Property("m_nDestChunk");
                    if (destChunk == -1)
                        continue;

                    var destInstruction = outflow.GetInt32Property("m_nInstruction");
                    if (destInstruction < 0) destInstruction = 0;

                    var sourceOutflowName = outflow.GetStringProperty("m_SourceOutflowName");

                    var outputSocket = new SocketOut(typeof(Action), outflowName, node);
                    node.Sockets.Add(outputSocket);
                    TraverseNodesForChunk(destChunk, outputSocket, destInstruction);
                }
            }
        }

        void PopulateSpecificCell(Node node, int cellIdx)
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
                            if (destChunk == -1)
                                break;

                            var destInstr = wakeResume.GetInt32Property("m_nInstruction");
                            if (destInstr < 0) destInstr = 0;

                            var outputSocket = new SocketOut(typeof(Action), "OnFinished", node);
                            node.Sockets.Add(outputSocket);
                            TraverseNodesForChunk(destChunk, outputSocket, destInstr);
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
                                        var destChunk = output.GetInt32Property("m_nDestChunk");
                                        if (destChunk == -1)
                                            continue;

                                        var outflowName = output.GetStringProperty("m_SourceOutflowName");
                                        var destInstruction = output.GetInt32Property("m_nInstruction");
                                        if (destInstruction < 0) destInstruction = 0;

                                        var outputSocket = new SocketOut(typeof(Action), outflowName, node);
                                        node.Sockets.Add(outputSocket);

                                        TraverseNodesForChunk(destChunk, outputSocket, destInstruction);
                                    }
                                    break;
                                }

                        }
                        HandleGenericOutflowsForCell(node, cellIdx);

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

                                    var publicOutput = publicOutputs[cellIdx];
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
                                        var destInstruction = eventOutflow.GetInt32Property("m_nInstruction");
                                        if (destInstruction < 0) destInstruction = 0;

                                        var timeFromPrevious = timelineEvent.GetFloatProperty("m_flTimeFromPrevious");
                                        var outputSocket = new SocketOut(typeof(Action), $"(Time from prev: {timeFromPrevious}s) | {outflowName}", node);
                                        node.Sockets.Add(outputSocket);

                                        TraverseNodesForChunk(destChunk, outputSocket, destInstruction);
                                    }

                                    HandleGenericOutflowsForCell(node, cellIdx);

                                    break;
                                }
                        }
                        // Some cells don't have Outflow in the name, but still have outflows.
                        HandleGenericOutflowsForCell(node, cellIdx);
                        break;
                    }
            }
        }

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
                            continue;

                        var entryChunkIdx = cells[cellIdx].GetInt32Property("m_EntryChunk");
                        registerSocketOutputMap.TryAdd(entryChunkIdx, []);

                        var outputSocket = new SocketOut(typeof(Action), "", cellNode);
                        cellNode.Sockets.Add(outputSocket);

                        if (cells[cellIdx].TryGetValue("m_RegisterMap", out var registerMap))
                        {
                            var outParams = registerMap["m_Outparams"];
                            if (!outParams.IsNull)
                            {
                                foreach (var outParam in outParams.AsEnumerable())
                                {
                                    var paramName = outParam.Key;
                                    var regIdx = (int)outParam.Value;

                                    var outSocket = new SocketOut(typeof(Value), paramName, cellNode);
                                    registerSocketOutputMap[entryChunkIdx][regIdx] = outSocket;
                                    cellNode.Sockets.Add(outSocket);
                                }
                            }
                        }

                        TraverseNodesForChunk(entryChunkIdx, outputSocket);
                        chunkFunctionName.Add(entryChunkIdx, cells[cellIdx].GetStringProperty("m_MethodName"));

                        var morethingies = FilterBaseCellFieldsForDisplay(cellIdx);
                        foreach (var kvPair in morethingies)
                        {
                            var val = kvPair.Value;
                            cellNode.AddText($"{kvPair.Key} = {StringifyKVObject(val)}");
                        }

                        nodeGraph.AddNode(cellNode);
                        break;
                    }
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

                    var outputSocket = new SocketOut(typeof(Action), "", cellNode);
                    cellNode.Sockets.Add(outputSocket);
                    chunkFunctionName.Add(chunkId, newName);
                    cellNode.AddText(newName);

                    TraverseNodesForChunk(chunkId, outputSocket);
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
        graphInfoNode.AddText($"Domain: {graphDefinition.GetStringProperty("m_DomainIdentifier")}");
        graphInfoNode.AddText($"Domain sub-type: {graphDefinition.GetStringProperty("m_DomainSubType")}");
        graphInfoNode.AddText($"Parent map: {graphDefinition.GetStringProperty("m_ParentMapName")}");
        graphInfoNode.AddText($"Parent XML panel: {graphDefinition.GetStringProperty("m_ParentXmlName")}");
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

        public SocketIn CreateSocketIn<T>(string text) where T : struct
        {
            var socket = new SocketIn(typeof(T), text, this, hub: true);
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
