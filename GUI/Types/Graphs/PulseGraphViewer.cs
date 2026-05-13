using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using HarfBuzzSharp;
using SkiaSharp;
using Svg.Skia;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;
using Windows.ApplicationModel.Resources.Management;
using static ValveResourceFormat.Blocks.ResourceIntrospectionManifest.ResourceDiskEnum;

namespace GUI.Types.Graphs;

using RegisterValueMap = Dictionary<int, KVObject>;
internal class PulseGraphViewer : GLNodeGraphViewer
{
    private static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);
    public static SKColor NodeColor { get; set; }
    public static SKColor NodeTextColor { get; set; }

    private readonly KVObject graphDefinition;

    enum GraphNodeType
    {
        Generic,
        Inflow,
        Outflow,
        Step,
        Value,
        Timeline,
    };

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

    struct RegisterInfo
    {
        int index;
        string? knownConstantValue;
    }
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
        // registers that have known loaded constant value.
        Dictionary<int, RegisterValueMap> staticCalculatedRegisterValues = [];
        Dictionary<int, Node> createdNodes = [];
        List<NodeCallInfo> callNodesToResolve = [];

        GraphNodeType GetCellType(int cellIdx)
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

            if (Enum.TryParse(nodeType, out GraphNodeType type))
            {
                return type;
            }

            return GraphNodeType.Generic;
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

        string GetCellNameSuffix(int cellIdx)
        {
            var cell = cells[cellIdx];
            var className = cell.GetStringProperty("_class");
            var index = className.LastIndexOf('_');
            if (index == -1)
            {
                return "Unknown";
            }

            return className[(index + 1)..];
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

        Node CreateNode(string[] nodePaths, IReadOnlyList<KVObject> nodes, int nodeIdx)
        {
            if (createdNodes.TryGetValue(nodeIdx, out var existingNode))
            {
                return existingNode;
            }

            var node = new Node(nodes[nodeIdx])
            {
                Name = $"({nodeIdx}) {GetCellNameSuffix(nodeIdx)}",
                NodeType = "Node",
            };

            nodeGraph.AddNode(node);
            createdNodes[nodeIdx] = node;
            return node;
        }

        // Filter out some internal fields, keep only what's derived from the base cell class and useful for display
        KVObject FilterBaseCellFieldsForDisplay(int cellIndex)
        {
            string[] filterKeys = ["_class", "m_EntryChunk", "m_nEditorNodeID", "m_RegisterMap"];
            var cell = cells[cellIndex];
            var filteredCell = new KVObject();

            foreach (var key in cell.Keys)
            {
                if (!filterKeys.Contains(key))
                {
                    filteredCell[key] = cell[key];
                }
            }
            return filteredCell;
        }

        SocketOut CreateSequentialActionSockets(Node node, SocketOut previousActionOutSocket)
        {
            var socketIn = node.CreateSocketIn<Action>("actionIn");
            nodeGraph.Connect(previousActionOutSocket, socketIn);

            var socketOut = new SocketOut(typeof(Action), "actionOut", node);
            node.Sockets.Add(socketOut);
            return socketOut;
        }

        void AddNodeRegisterInput(Node node, int chunkIndex, Dictionary<int, SocketOut> registerSocketOutputMap, int regIndex)
        {
            if (staticCalculatedRegisterValues[chunkIndex].TryGetValue(regIndex, out var regValue))
            {
                node.AddText($"\"{regValue.ToString()}\"");
            }
            else
            {
                var regOutSocket = registerSocketOutputMap[regIndex];
                var argInputSocket = node.CreateSocketIn<Value>("value");
                nodeGraph.Connect(regOutSocket, argInputSocket);
            }
        }

        void CreateInputsForNodeWithInvokeBinding(Node node, int chunkIndex, Dictionary<int, SocketOut> registerSocketOutputMap, KVObject registerMap)
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
                    node.AddText($"{regName} = {regValue}");
                }
            }
        }

        // Populates relevant information and creates node graphs for the given chunk
        void TraverseNodesForChunk(int chunkIndex, SocketOut sourceActionOutSocket, int startingInstruction = 0)
        {
            staticCalculatedRegisterValues.TryAdd(chunkIndex, []);
            var chunk = chunks[chunkIndex];
            var instructions = chunk.GetArray("m_Instructions");
            var registers = chunk.GetArray("m_Registers");

            // Cache registers to output sockets that are calculated by a node.
            Dictionary<int, SocketOut> registerSocketOutputMap = [];

            SocketOut previousActionOutSocket = sourceActionOutSocket;
            bool stopProcessing = false;
            foreach (var instruction in instructions.Skip(startingInstruction))
            {
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
                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket);
                            }
                            else // In other case cache the register output values and create output sockets.
                            {
                                var outParams = registerMap["m_Outparams"];
                                foreach (var (paramName, regIdx) in outParams)
                                {
                                    var regValue = (int)regIdx;

                                    var outSocket = new SocketOut(typeof(Value), paramName, node);
                                    registerSocketOutputMap[regValue] = outSocket;
                                    node.Sockets.Add(outSocket);
                                }
                            }

                            CreateInputsForNodeWithInvokeBinding(node, chunkIndex, registerSocketOutputMap, registerMap);

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
                            var cellName = GetCellNameSuffix(cellIndex);
                            var cellType = GetCellType(cellIndex);
                            var node = new Node(null)
                            {
                                Name = cellName,
                                NodeType = cellType.ToString(),
                            };

                            // If an action without outputs then create a new output action and continue.
                            if (registerMap["m_Outparams"].IsNull)
                            {
                                previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket);
                            }
                            else // In other case cache the register output values and create output sockets.
                            {
                                var outParams = registerMap["m_Outparams"];
                                foreach (var (paramName, regIdx) in outParams)
                                {
                                    var regValue = (int)regIdx;

                                    var outSocket = new SocketOut(typeof(Value), paramName, node);
                                    registerSocketOutputMap[regValue] = outSocket;
                                    node.Sockets.Add(outSocket);
                                }
                            }

                            var morethingies = FilterBaseCellFieldsForDisplay(cellIndex);
                            foreach (var kvPair in morethingies)
                            {
                                var val = kvPair.Value.ToString();
                                node.AddText($"{kvPair.Key} = \"{val}\"");
                            }

                            CreateInputsForNodeWithInvokeBinding(node, chunkIndex, registerSocketOutputMap, registerMap);
                            PopulateNonInflowCell(node, cellIndex);

                            nodeGraph.AddNode(node);
                            break;
                        }
                    case InstructionType.GET_CONST:
                        {
                            var constIdx = instruction.GetInt32Property("m_nConstIdx");
                            var outputRegIdx = instruction.GetInt32Property("m_nReg0");
                            staticCalculatedRegisterValues[chunkIndex][outputRegIdx] = GetConstantValueFromId(constIdx);
                            break;
                        }
                    case InstructionType.GET_DOMAIN_VALUE:
                        {
                            var domainValIdx = instruction.GetInt32Property("m_nDomainValueIdx");
                            var outputRegIdx = instruction.GetInt32Property("m_nReg0");
                            staticCalculatedRegisterValues[chunkIndex][outputRegIdx] = GetDomainValueFromId(domainValIdx);
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
                            registerSocketOutputMap[regIndex] = sockOut;
                            node.Sockets.Add(sockOut);

                            nodeGraph.AddNode(node);
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
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket);

                            node.AddText(GetVariableNameFromIndex(varIndex));
                            AddNodeRegisterInput(node, chunkIndex, registerSocketOutputMap, regIndex);

                            nodeGraph.AddNode(node);
                            break;
                        }
                    case InstructionType.PULSE_CALL_SYNC:
                    case InstructionType.PULSE_CALL_ASYNC_FIRE:
                        {
                            var callTargetChunk = instruction.GetInt32Property("m_nChunk");
                            var node = new Node(null)
                            {
                                Name = instrType == InstructionType.PULSE_CALL_SYNC ? "Call" : "Call Asynchronously",
                                NodeType = "Flow",
                            };
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket);

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
                            previousActionOutSocket = CreateSequentialActionSockets(node, previousActionOutSocket);
                            AddNodeRegisterInput(node, chunkIndex, registerSocketOutputMap, regIndex);
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
                            TraverseNodesForChunk(chunkIndex, previousActionOutSocket, destInstruction);
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
                                        node.AddText($"arg1 = \"{reg1Val.ToString()}\"");
                                    }

                                    if (!reg1Calculated)
                                    {
                                        // if it doesn't exist then instruction registers were defined out of order, or we didn't calculate one.
                                        // TODO: handle this case wtihout crashing.
                                        var regOutSocket = registerSocketOutputMap[reg1];
                                        var argInputSocket = node.CreateSocketIn<Value>("arg1");
                                        nodeGraph.Connect(regOutSocket, argInputSocket);
                                    }
                                }

                                if (reg2 != -1)
                                {
                                    if (staticCalculatedRegisterValues[chunkIndex].TryGetValue(reg2, out var reg2Val))
                                    {
                                        reg2Calculated = true;
                                        node.AddText($"arg2 = \"{reg2Val.ToString()}\"");
                                    }

                                    if (!reg2Calculated)
                                    {
                                        // if it doesn't exist then instruction registers were defined out of order, or we didn't calculate one.
                                        // TODO: handle this case wtihout crashing.
                                        var regOutSocket = registerSocketOutputMap[reg2];
                                        var argInputSocket = node.CreateSocketIn<Value>("arg2");
                                        nodeGraph.Connect(regOutSocket, argInputSocket);
                                    }
                                }

                                // create output socket for this node, and store it for future connections
                                var socketOut = new SocketOut(typeof(Value), "retval", node);
                                node.Sockets.Add(socketOut);
                                registerSocketOutputMap[reg0] = socketOut;

                                nodeGraph.AddNode(node);
                            }

                            break;
                        }

                }
            }
        }

        void PopulateNonInflowCell(Node node, int cellIdx)
        {
            var cellType = GetCellType(cellIdx);
            var cellName = GetCellNameSuffix(cellIdx);

            switch (cellType)
            {
                case GraphNodeType.Timeline:
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

                            node.AddText($"Time from previous: {timeFromPrevious}");
                            var outputSocket = new SocketOut(typeof(Action), outflowName, node);
                            node.Sockets.Add(outputSocket);

                            TraverseNodesForChunk(destChunk, outputSocket, destInstruction);
                        }

                        var onFinished = cells[cellIdx]["m_OnFinished"];
                        var onFinishedDestChunk = onFinished.GetInt32Property("m_nDestChunk");
                        if (onFinishedDestChunk != -1)
                        {
                            var outputSocket = new SocketOut(typeof(Action), "OnFinished", node);
                            node.Sockets.Add(outputSocket);
                            TraverseNodesForChunk(onFinishedDestChunk, outputSocket);
                        }

                        break;
                    }
                case GraphNodeType.Inflow:
                    {
                        // here we assume that wait is going to be processed sequentially, not out of order, even though it's theorithically possible.
                        if (cellName == "Wait")
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
                case GraphNodeType.Outflow:
                    {
                        // eugh
                        if (cellName == "CycleRandom" || cellName == "CycleShuffled" || cellName == "CycleOrdered")
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
                        }
                        break;
                    }
            }
        }

        // Inflow cells
        for (var cellIdx = 0; cellIdx < cells.Count; cellIdx++)
        {
            var cellType = GetCellType(cellIdx);
            var cellName = GetCellNameSuffix(cellIdx);
            var cellNode = new Node(null)
            {
                Name = cellName,
                NodeType = cellType.ToString(),
            };

            switch (cellType)
            {
                case GraphNodeType.Inflow:
                    {
                        if (!cells[cellIdx].ContainsKey("m_EntryChunk"))
                            continue;

                        var entryChunkIdx = cells[cellIdx].GetInt32Property("m_EntryChunk");
                        var outputSocket = new SocketOut(typeof(Action), "actionOut", cellNode);
                        cellNode.Sockets.Add(outputSocket);

                        TraverseNodesForChunk(entryChunkIdx, outputSocket);

                        var morethingies = FilterBaseCellFieldsForDisplay(cellIdx);
                        foreach (var kvPair in morethingies)
                        {
                            var val = kvPair.Value.ToString();
                            cellNode.AddText($"{kvPair.Key} = \"{val}\"");
                        }

                        nodeGraph.AddNode(cellNode);
                        break;
                    }
            }
        }

        // Resolve call nodes to display the target function name
        foreach (var callNodeInfo in callNodesToResolve)
        {
            var targetChunk = callNodeInfo.targetChunk;
            string methodNameToCall = "<Unknown>";
            foreach (var cell in cells)
            {
                if (cell.GetInt32Property("m_EntryChunk") == targetChunk)
                {
                    methodNameToCall = cell.GetStringProperty("m_MethodName");
                    break;
                }
            }
            methodNameToCall = $"\"{methodNameToCall}\"";
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
            var socket = new SocketIn(typeof(T), text, this, hub: false);
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
