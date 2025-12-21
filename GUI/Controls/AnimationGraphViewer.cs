using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using NodeGraphControl.Elements;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers;

internal class AnimationGraphViewer : NodeGraphControl.NodeGraphControl
{
    private readonly VrfGuiContext vrfGuiContext;
    private readonly KVObject graphDefinition;

    public AnimationGraphViewer(VrfGuiContext guiContext, KVObject data) : base()
    {
        vrfGuiContext = guiContext;
        graphDefinition = data;

        Dock = DockStyle.Fill;
        GridStyle = EGridStyle.Grid;

        CanvasBackgroundColor = Color.FromArgb(40, 40, 40);
        NodeColor = Color.FromArgb(60, 60, 60);
        NodeTextColor = Color.FromArgb(230, 230, 230);
        GridColor = Color.White;

        PoseColor = Color.LightGreen;
        ValueColor = Color.LightBlue;

        if (Themer.CurrentTheme == Themer.AppTheme.Dark)
        {
            CanvasBackgroundColor = Themer.CurrentThemeColors.AppMiddle;
            NodeColor = Themer.CurrentThemeColors.AppSoft;
            GridColor = Themer.CurrentThemeColors.ContrastSoft;

            PoseColor = ControlPaint.Dark(PoseColor);
            ValueColor = ControlPaint.Dark(ValueColor);
        }

        AddTypeColorPair<Pose>(PoseColor);
        AddTypeColorPair<Value>(ValueColor);

        CreateGraph();
    }

    private struct Pose;
    private struct Value;

    private bool firstPaint = true;
    public static Color NodeColor { get; set; }
    public static Color NodeTextColor { get; set; }
    public static Color PoseColor { get; set; }
    public static Color ValueColor { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (firstPaint)
        {
            firstPaint = false;
            FocusView(PointF.Empty);
        }

        base.OnPaint(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        var element = FindElementAtMousePoint(e.Location);

        if (element is Node { ExternalResourceName: not null } node)
        {
            var foundFile = vrfGuiContext.FindFileWithContext(node.ExternalResourceName + ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);
            if (foundFile.Context != null)
            {
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
            }
        }
    }

    private void CreateGraph()
    {
        var rootNodeIdx = graphDefinition.GetInt32Property("m_nRootNodeIdx");
        var nodePaths = graphDefinition.GetArray<string>("m_nodePaths");
        var nodes = graphDefinition.GetArray("m_nodes");

        string GetName(int nodeIdx)
        {
            return nodePaths[nodeIdx].Split('/')[^1];
        }

        string GetType(int nodeIdx)
        {
            var className = nodes[nodeIdx].GetStringProperty("_class");
            const string Preffix = "CNm";
            const string Suffix = "Node::CDefinition";
            var @type = className[Preffix.Length..^Suffix.Length];
            return @type;
        }

        static void CalculateChildNodeLocation(Node node, int totalChildren, Node childNode, int childNodeHeight, int horizontalOffset = 300)
        {
            var childIndex = node.Sockets.Count;
            horizontalOffset += totalChildren * 15; // offset for each child node
            childNode.Location = node.Location - new Size(horizontalOffset, totalChildren * childNodeHeight / 2 - childIndex * childNodeHeight);
        }

        Dictionary<int, Node> createdNodes = new(nodes.Length);

        Node CreateNode(string[] nodePaths, KVObject[] nodes, int nodeIdx)
        {
            if (createdNodes.TryGetValue(nodeIdx, out var existingNode))
            {
                return existingNode;
            }

            var node = new Node(nodes[nodeIdx])
            {
                Name = $"({nodeIdx}) {GetName(nodeIdx)}",
                NodeType = GetType(nodeIdx),
            };

            AddNode(node);
            createdNodes[nodeIdx] = node;
            return node;
        }

        // Used for calculating some node position.
        var depth = 0;
        var previousChildLocation = new Point(0, 0);
        var previousChildHeight = 0;

        (Node, SocketIn) CreateInputAndChild<ValueType>(Node parent, int totalChildren, int nodeIdx, int height = 100, int offset = 300, string? parentInputName = null, string? childOutputName = null, bool hub = false)
            where ValueType : struct
        {
            var (childNode, childNodeOutput) = CreateChild<ValueType>(parent, totalChildren, nodeIdx, height, offset, childOutputName);

            var input = new SocketIn(typeof(ValueType), parentInputName ?? childNode.Name, parent, hub: hub);
            parent.Sockets.Add(input);
            Connect(childNodeOutput, input);

            return (childNode, input);
        }

        (Node, SocketOut) CreateChild<ValueType>(Node parent, int totalChildren, int nodeIdx, int height, int offset = 300, string? childOutputName = null)
        {
            var childNode = CreateNode(nodePaths, nodes, nodeIdx);

            // child node already exists, all we do is connect to its existing output.
            if (childNode.Sockets.Count > 0)
            {
                if (childNode.Sockets.FirstOrDefault(s => s is SocketOut) is SocketOut output)
                {
                    return (childNode, output);
                }
            }

            var moreWidthFurtherDeep = depth * 70;
            var extraHeightCloseDepth = 200 / (int)Math.Pow(depth + 1, 1.5f);

            height = Math.Max(height, previousChildHeight + 20);

            if (childNode.NodeType is "Clip" or "ReferencedGraph")
            {
                childOutputName = string.Empty;
            }

            var childNodeOutput = new SocketOut(typeof(ValueType), childOutputName ?? string.Empty, childNode);
            childNode.Sockets.Add(childNodeOutput);
            CalculateChildNodeLocation(parent, totalChildren, childNode, height + Random.Shared.Next(0, 20) + extraHeightCloseDepth, offset + Random.Shared.Next(0, 20) + moreWidthFurtherDeep);

            if (previousChildHeight > 0)
            {
                // No vertical overlap
                childNode.Location = new Point(childNode.Location.X, Math.Max(childNode.Location.Y, previousChildLocation.Y + height));
            }

            try
            {
                depth += 1;
                CreateChildren(childNode, nodeIdx);
                depth -= 1;
            }
            catch (IndexOutOfRangeException)
            {
                Console.WriteLine($"Error creating children for {childNode.Name} (idx = {nodeIdx}).");
            }

            childNode.Calculate(); // node and wire position
            previousChildLocation = childNode.Location;
            previousChildHeight = (int)childNode.BoundsFull.Height;
            return (childNode, childNodeOutput);
        }

        void CreateChildren(Node node, int nodeIdx)
        {
            var data = nodes[nodeIdx];
            previousChildHeight = 0;

            if (node.NodeType == "StateMachine")
            {
                var children = data.GetArray("m_stateDefinitions");
                foreach (var stateDefinition in children)
                {
                    var stateNodeIdx = stateDefinition.GetInt32Property("m_nStateNodeIdx");
                    var entryConditionNodeIdx = stateDefinition.GetInt32Property("m_nEntryConditionNodeIdx"); // can be -1
                    // m_transitionDefinitions

                    var stateName = GetName(stateNodeIdx);
                    var stateNode = nodes[stateNodeIdx];
                    var stateInputIdx = stateNode.GetInt32Property("m_nChildNodeIdx");

                    var input = new SocketIn(typeof(Pose), stateName, node, hub: true);
                    node.Sockets.Add(input);

                    if (stateInputIdx != -1)
                    {
                        var (_, stateNodeOut) = CreateChild<Pose>(node, children.Length, stateInputIdx, 150, 500);
                        Connect(stateNodeOut, input);
                    }

                    if (entryConditionNodeIdx != -1)
                    {
                        var (_, childOutput) = CreateChild<Value>(node, children.Length, entryConditionNodeIdx, 80, 300, stateName);
                        Connect(childOutput, input);
                    }
                }
            }
            else if (node.NodeType is "ParameterizedSelector" or "ParameterizedClipSelector")
            {
                var options = data.GetArray<int>("m_optionNodeIndices");

                var parameterNodeIdx = data.GetInt32Property("m_parameterNodeIdx");
                CreateInputAndChild<Value>(node, options.Length + 1, parameterNodeIdx, 120, 300);

                var hasWeightsSet = data.GetProperty<bool>("m_bHasWeightsSet");
                var totalWeight = 0;
                var weights = data.GetArray<uint>("m_optionWeights");

                if (hasWeightsSet)
                {
                    totalWeight = Math.Max(1, weights.Cast<int>().Sum());
                }

                var i = 0;
                foreach (var optionNodeIdx in options)
                {
                    var weightDesc = string.Empty;
                    if (hasWeightsSet)
                    {
                        // If weights are set, show the weight and its percentage.
                        var weight = weights[i];
                        var weightPercentage = weight / (float)totalWeight * 100;

                        weightDesc = $"Weight: {weight} ({weightPercentage:F2}%)";
                    }

                    CreateInputAndChild<Pose>(node, options.Length + 1, optionNodeIdx, 80, 300, $"Option {++i} {weightDesc}");
                }
            }
            else if (node.NodeType is "Selector" or "ClipSelector")
            {
                // Select the first option for which the condition passes?
                var options = data.GetArray<int>("m_optionNodeIndices");
                var conditions = data.GetArray<int>("m_conditionNodeIndices");

                var i = 0;
                foreach (var (optionNodeIdx, conditionNodeIdx) in options.Zip(conditions))
                {
                    var (_, optionInput) = CreateInputAndChild<Pose>(node, options.Length, optionNodeIdx, 80, 300, hub: true);
                    var (_, conditionOutput) = CreateChild<Value>(node, options.Length, conditionNodeIdx, 80, 300);
                    Connect(conditionOutput, optionInput);
                    i++;
                }
            }
            else if (node.NodeType is "LayerBlend")
            {
                var baseNodeIdx = data.GetInt32Property("m_nBaseNodeIdx");
                CreateInputAndChild<Pose>(node, 3, baseNodeIdx, 100, 300, "Base", "Result");

                var layerInput = new SocketIn(typeof(Pose), "Layers", node, true);
                node.Sockets.Add(layerInput);

                var layerDefinition = data.GetArray("m_layerDefinition");
                var layerIndex = 0;
                foreach (var layer in layerDefinition)
                {
                    var layerNode = AddNode(new Node(layerDefinition[layerIndex])
                    {
                        Name = $"Layer{layerIndex}",
                        NodeType = "_LayerDefinition_",
                    });

                    CalculateChildNodeLocation(node, layerDefinition.Length, layerNode, 100, 300);
                    layerNode.Location = new Point(layerNode.Location.X, layerNode.Location.Y + 140 * layerIndex);

                    var layerOutput = new SocketOut(typeof(Pose), string.Empty, layerNode);
                    layerNode.Sockets.Add(layerOutput);
                    Connect(layerOutput, layerInput);
                    CreateInputAndChild<Pose>(layerNode, 1, layer.GetInt32Property("m_nInputNodeIdx"), 100, 400);

                    // Optional inputs
                    var weightNodeIdx = layer.GetInt32Property("m_nWeightValueNodeIdx");
                    var boneMaskNodeIdx = layer.GetInt32Property("m_nBoneMaskValueNodeIdx");
                    var rootMotionNodeIdx = layer.GetInt32Property("m_nRootMotionWeightValueNodeIdx");

                    if (weightNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(layerNode, 1, weightNodeIdx, parentInputName: "Weight");
                    }

                    if (boneMaskNodeIdx != -1)
                    {
                        CreateInputAndChild<Pose>(layerNode, 1, boneMaskNodeIdx, parentInputName: "Bone Mask");
                    }

                    if (rootMotionNodeIdx != -1)
                    {
                        CreateInputAndChild<Pose>(layerNode, 1, rootMotionNodeIdx, parentInputName: "Root Motion");
                    }

                    layerNode.AddText($"Is Synchronized: {layer.GetProperty<bool>("m_bIsSynchronized")}");
                    layerNode.AddText($"Ignore Events: {layer.GetProperty<bool>("m_bIgnoreEvents")}");
                    layerNode.AddText($"Is State Machine Layer: {layer.GetProperty<bool>("m_bIsStateMachineLayer")}");
                    layerNode.AddText($"Blend Mode: {layer.GetStringProperty("m_blendMode")}");
                    layerNode.Calculate();
                    layerIndex++;
                }
            }
            else if (node.NodeType is "Blend1D" or "Blend2D")
            {
                var sourceNodeIndices = data.GetArray<int>("m_sourceNodeIndices");
                var childCount = sourceNodeIndices.Length + 2;

                if (node.NodeType == "Blend1D")
                {
                    var inputNodeIdx = data.GetInt32Property("m_nInputParameterValueNodeIdx");
                    CreateInputAndChild<Value>(node, childCount, inputNodeIdx, 70, 300, "Parameter");
                }
                else if (node.NodeType == "Blend2D")
                {
                    var inputNodeIdxA = data.GetInt32Property("m_nInputParameterNodeIdx0");
                    var inputNodeIdxB = data.GetInt32Property("m_nInputParameterNodeIdx1");

                    childCount += 1;
                    CreateInputAndChild<Value>(node, childCount, inputNodeIdxA, 70, 300, "Parameter A");
                    CreateInputAndChild<Value>(node, childCount, inputNodeIdxB, 70, 300, "Parameter B");
                }

                var optionIndex = 0;
                foreach (var sourceNodeIdx in sourceNodeIndices)
                {
                    CreateInputAndChild<Pose>(node, childCount, sourceNodeIdx, 100, 300, $"Option {++optionIndex}");
                }

                node.AddText($"Allow Looping: {data.GetProperty<bool>("m_bAllowLooping")}");
            }
            else if (node.NodeType is "BoneMask")
            {
                node.AddText(data.GetProperty<string>("m_boneMaskID"));
            }
            else if (node.NodeType is "ConstTarget")
            {
                var value = data.GetProperty<KVObject>("m_value");
                var boneId = value.GetProperty<string>("m_boneID");
                var isBoneTarget = value.GetProperty<bool>("m_bIsBoneTarget");
                var isUsingBoneSpaceOffsets = value.GetProperty<bool>("m_bIsUsingBoneSpaceOffsets");
                var hasOffsets = value.GetProperty<bool>("m_bHasOffsets");
                var isSet = value.GetProperty<bool>("m_bIsSet");

                node.AddText($"Bone: {boneId}");
                node.AddText($"Is Bone Target: {isBoneTarget}");
                node.AddText($"Bone Space Offsets: {isUsingBoneSpaceOffsets}");
                node.AddText($"Has Offsets: {hasOffsets}");
                node.AddText($"Is Set: {isSet}");
            }
            else if (node.NodeType is "SpeedScale")
            {
                CreateInputAndChild<Pose>(node, 3, data.GetInt32Property("m_nChildNodeIdx"), 100, 300, "Input");
                CreateInputAndChild<Value>(node, 3, data.GetInt32Property("m_nInputValueNodeIdx"), 100, 300, "Scale Value");
                node.AddText($"Default Scale: {data.GetFloatProperty("m_flDefaultInputValue")}");
            }
            else if (node.NodeType is "Not" or "FloatCurve")
            {
                CreateInputAndChild<Value>(node, 1, data.GetInt32Property("m_nInputValueNodeIdx"), 100, 300, "Value");

                // curve
            }
            else if (node.NodeType is "FloatRemap")
            {
                CreateInputAndChild<Value>(node, 1, data.GetInt32Property("m_nInputValueNodeIdx"), 100, 300, "Value");
                var inputRange = data.GetProperty<KVObject>("m_inputRange");
                var outputRange = data.GetProperty<KVObject>("m_outputRange");
                node.AddText($"InputBegin: {inputRange.GetFloatProperty("m_flBegin")} InputEnd: {inputRange.GetFloatProperty("m_flEnd")}");
                node.AddText($"OutputBegin: {outputRange.GetFloatProperty("m_flBegin")} OutputEnd: {outputRange.GetFloatProperty("m_flEnd")}");
            }
            else if (node.NodeType is "IDEventCondition")
            {
                var eventIds = data.GetArray<string>("m_eventIDs");
                var sourceStateNodeIdx = data.GetInt32Property("m_nSourceStateNodeIdx");
                if (sourceStateNodeIdx != -1)
                {
                    node.AddText($"State: {GetName(sourceStateNodeIdx)}");
                }

                foreach (var eventId in eventIds)
                {
                    node.AddText($"Event: '{eventId}'");
                }
            }
            else if (node.NodeType.EndsWith("Math"))
            {
                var inputNodeIdxA = data.GetProperty("m_nInputValueNodeIdxA", -1);
                var inputNodeIdxB = data.GetProperty("m_nInputValueNodeIdxB", -1);

                CreateInputAndChild<Value>(node, 2, inputNodeIdxA, 100, 300, "A");

                var @operator = data.GetProperty<string>("m_operator");
                node.AddText(@operator);

                if (inputNodeIdxB != -1)
                {
                    CreateInputAndChild<Value>(node, 2, inputNodeIdxB, 100, 300, "B");
                }
                else
                {
                    if (node.NodeType == "FloatMath")
                    {
                        node.AddText($"{data.GetFloatProperty("m_flValueB"):f}");
                    }
                }
            }
            else if (node.NodeType.EndsWith("Comparison"))
            {
                var childNodeIdx = data.GetInt32Property("m_nInputValueNodeIdx");
                CreateInputAndChild<Value>(node, 1, childNodeIdx, 100, 300, GetName(childNodeIdx));

                if (data.ContainsKey("m_comparison"))
                {
                    var comparison = data.GetProperty<string>("m_comparison");
                    node.AddText(comparison);
                }

                if (node.NodeType is "IDComparison")
                {
                    var comparisonIds = data.GetArray<string>("m_comparisionIDs");
                    foreach (var comparisonId in comparisonIds)
                    {
                        node.AddText($"'{comparisonId}'");
                    }
                }
                else if (node.NodeType is "FloatComparison" or "IntComparison")
                {
                    var comparandNodeIdx = data.GetInt32Property("m_nComparandValueNodeIdx");
                    if (comparandNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, 1, comparandNodeIdx, 100, 300, "Comparand");
                    }
                    else
                    {
                        node.AddText($"{data.GetFloatProperty("m_flComparisonValue"):f}");
                    }
                }
                else
                {
                    Console.WriteLine($"Generic handled node: {node.NodeType} ({node.Name})");
                }
            }
            else if (node.Data.ContainsKey("m_conditionNodeIndices")) // Conditional node
            {
                var conditions = data.GetArray<int>("m_conditionNodeIndices");
                foreach (var condition in conditions)
                {
                    CreateInputAndChild<Value>(node, conditions.Length + 1, condition, 80);
                }
            }
            else if (node.Data.ContainsKey("m_nChildNodeIdx"))
            {
                var childCount = 1;
                if (node.NodeType == "Scale")
                {
                    childCount = 3;
                    CreateInputAndChild<Pose>(node, childCount, data.GetInt32Property("m_nMaskNodeIdx"), 130, 300, "Mask");
                    CreateInputAndChild<Value>(node, childCount, data.GetInt32Property("m_nEnableNodeIdx"), 130, 300, "Enable");
                }
                else if (node.NodeType == "TwoBoneIK")
                {
                    childCount = 2;
                    node.AddText($"Bone: {data.GetProperty<string>("m_effectorBoneID")}");
                    CreateInputAndChild<Pose>(node, childCount, data.GetInt32Property("m_nEffectorTargetNodeIdx"), 100, 300, "Effector");
                    var enabledNodeIdx = data.GetInt32Property("m_nEnabledNodeIdx");
                    if (enabledNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, childCount, enabledNodeIdx, 100, 300, "Enabled");
                    }
                    else
                    {
                        node.AddText("Enabled: true");
                    }
                    node.AddText($"Blend Time: {data.GetFloatProperty("m_flBlendTimeSeconds"):f}");
                    node.AddText($"Blend Mode: {data.GetProperty<string>("m_blendMode")}");
                    node.AddText($"Worldspace: {data.GetProperty<bool>("m_bIsTargetInWorldSpace")}");
                }

                var childNodeIdx = data.GetInt32Property("m_nChildNodeIdx");
                CreateInputAndChild<Pose>(node, childCount, childNodeIdx, 100, 300, "Input", "Result");
            }
            else if (node.NodeType is "Clip" or "AnimationPose" or "ReferencedGraph")
            {
                // Debug.Assert output type is Pose
                var resources = graphDefinition.GetArray<string>("m_resources");

                var referencedGraphIdx = data.GetInt32Property("m_nReferencedGraphIdx");
                var referencedGraphSlots = graphDefinition.GetArray("m_referencedGraphSlots");
                var dataSlotIdx = data.GetInt32Property("m_nDataSlotIdx");

                if (node.NodeType is "ReferencedGraph")
                {
                    dataSlotIdx = referencedGraphSlots[referencedGraphIdx].GetInt32Property("m_dataSlotIdx");

                    var fallbackNodeIdx = data.GetInt32Property("m_nFallbackNodeIdx");
                    if (fallbackNodeIdx != -1)
                    {
                        node.AddSpace();
                        CreateInputAndChild<Pose>(node, 1, fallbackNodeIdx, 100, 300, "Fallback");
                    }
                }
                else if (node.NodeType is "Clip")
                {
                    node.AddSpace();
                    node.AddText($"Speed: {data.GetFloatProperty("m_flSpeedMultiplier"):F2}x");
                    node.AddText($"StartSyncEvent Offset: {data.GetInt32Property("m_nStartSyncEventOffset")}");
                    node.AddText($"Sample RootMotion: {data.GetProperty<bool>("m_bSampleRootMotion")}");
                    node.AddText($"Allow Looping: {data.GetProperty<bool>("m_bAllowLooping")}");

                    var playInReverseNodeIdx = data.GetInt32Property("m_nPlayInReverseValueNodeIdx");
                    if (playInReverseNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, 1, playInReverseNodeIdx, 100, 300, "Play in reverse");
                    }

                    var resetTimeValueNodeIdx = data.GetInt32Property("m_nResetTimeValueNodeIdx");
                    if (resetTimeValueNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, 1, resetTimeValueNodeIdx, 100, 300, "Reset time");
                    }

                }
                else if (node.NodeType is "AnimationPose")
                {
                    node.AddSpace();

                    var poseTimeNodeIdx = data.GetInt32Property("m_nPoseTimeValueNodeIdx");
                    if (poseTimeNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, 1, poseTimeNodeIdx, 60, 300, "Time");
                    }

                    var timeRemapRange = data.GetProperty<KVObject>("m_inputTimeRemapRange");
                    var remapMin = timeRemapRange.GetFloatProperty("m_flMin");
                    var remapMax = timeRemapRange.GetFloatProperty("m_flMax");
                    var remapMinDesc = remapMin == float.MaxValue ? "None" : $"{remapMin:f}";
                    var remapMaxDesc = remapMax == float.MinValue ? "None" : $"{remapMax:f}";

                    node.AddText($"Remap: {remapMinDesc} - {remapMaxDesc}");
                    node.AddText($"Const Time: {data.GetFloatProperty("m_flUserSpecifiedTime"):f}");
                    node.AddText($"Use frames: {data.GetProperty<bool>("m_bUseFramesAsInput")}");
                }

                if (dataSlotIdx != -1)
                {
                    node.ExternalResourceName = resources[dataSlotIdx];
                }
            }
            else if (node.NodeType.StartsWith("ControlParameter"))
            {
                node.Description = "Graph input value set by game code.";
            }
            else
            {
                Console.WriteLine($"Unhandled node type: {node.NodeType} ({node.Name})");
            }

            node.Calculate();
        }

        var root = CreateNode(nodePaths, nodes, rootNodeIdx);
        root.StartNode = true;

        CreateChildren(root, rootNodeIdx);

        LayoutNodes(30f);
        Log.Debug(nameof(AnimationGraphViewer), $"Created {createdNodes.Count} nodes (out of {nodes.Length}) or {createdNodes.Count / (float)nodes.Length:P}.");
    }

    #region Nodes

    class Node : AbstractNode
    {
        public KVObject Data { get; set; }

        // todo: polymorphism
        public string? ExternalResourceName { get; set; }

        public Node(KVObject data)
        {
            Data = data;
            BaseColor = NodeColor;
            HeaderColor = ControlPaint.Light(NodeColor);
            TextColor = NodeTextColor;
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

        public override bool IsReady() => true;
        public override void Execute() { }

        public override void Draw(Graphics g)
        {
            base.Draw(g);

            if (string.IsNullOrEmpty(ExternalResourceName))
            {
                return;
            }

            using var font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Regular);

            var position = new PointF
            {
                X = Location.X + 3,
                Y = Location.Y + 55
            };

            var fileExtensionStart = ExternalResourceName.LastIndexOf('.');
            var trimStr = ExternalResourceName[..fileExtensionStart];
            trimStr = trimStr.Replace(".vnmgraph", string.Empty, StringComparison.Ordinal);
            trimStr = trimStr.Split('/').LastOrDefault() ?? trimStr;
            if (trimStr.Length > 23)
            {
                trimStr = '…' + trimStr[^22..];
            }

            using var brush = new SolidBrush(PoseColor);
            g.DrawString(trimStr, base.SocketCaptionFont, brush, position);
        }
    }

    #endregion Nodes
}
