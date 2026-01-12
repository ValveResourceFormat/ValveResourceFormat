using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;
using Svg.Skia;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Graphs;

internal class AnimationGraphViewer : GLNodeGraphViewer
{
    private readonly KVObject graphDefinition;

    public AnimationGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
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
        NodeColor = new SKColor(60, 60, 60);
        NodeTextColor = new SKColor(230, 230, 230);
        nodeGraph.GridColor = SKColors.White;

        PoseColor = new SKColor(173, 255, 47);
        ValueColor = new SKColor(0, 191, 255);

        if (Themer.CurrentTheme == Themer.AppTheme.Dark)
        {
            nodeGraph.CanvasBackgroundColor = ToSKColor(Themer.CurrentThemeColors.AppMiddle);
            NodeColor = ToSKColor(Themer.CurrentThemeColors.AppSoft);
            nodeGraph.GridColor = ToSKColor(Themer.CurrentThemeColors.ContrastSoft);
            //PoseColor = ToSKColor(ControlPaint.Dark(Color.LightGreen, 0f));
            //ValueColor = ToSKColor(ControlPaint.Dark(Color.LightBlue, 0f));
        }

        NodeGraphControl.AddTypeColorPair<Pose>(PoseColor);
        NodeGraphControl.AddTypeColorPair<Value>(ValueColor);

        return nodeGraph;
    }

    private struct Pose;
    private struct Value;

    private static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);

    public static SKColor NodeColor { get; set; }
    public static SKColor NodeTextColor { get; set; }
    public static SKColor PoseColor { get; set; }
    public static SKColor ValueColor { get; set; }

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
            const string Prefix = "CNm";
            const string Suffix = "Node::CDefinition";
            var @type = className[Prefix.Length..^Suffix.Length];
            return @type;
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

            nodeGraph.AddNode(node);
            createdNodes[nodeIdx] = node;
            return node;
        }


        (Node, SocketIn) CreateInputAndChild<ValueType>(Node parent, int totalChildren, int nodeIdx, string? parentInputName = null, string? childOutputName = null, bool hub = false)
            where ValueType : struct
        {
            var (childNode, childNodeOutput) = CreateChild<ValueType>(parent, totalChildren, nodeIdx, childOutputName);

            var input = new SocketIn(typeof(ValueType), parentInputName ?? childNode.Name, parent, hub: hub);
            parent.Sockets.Add(input);
            nodeGraph.Connect(childNodeOutput, input);

            return (childNode, input);
        }

        (Node, SocketOut) CreateChild<ValueType>(Node parent, int totalChildren, int nodeIdx, string? childOutputName = null)
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

            if (childNode.NodeType is "Clip" or "ReferencedGraph")
            {
                childOutputName = string.Empty;
            }

            var childNodeOutput = new SocketOut(typeof(ValueType), childOutputName ?? string.Empty, childNode);
            childNode.Sockets.Add(childNodeOutput);

            try
            {
                CreateChildren(childNode, nodeIdx);
            }
            catch (IndexOutOfRangeException)
            {
                Log.Error(nameof(AnimationGraphViewer), $"Error creating children for {childNode.Name} (idx = {nodeIdx}).");
            }

            childNode.UpdateTypeColorFromOutput();
            childNode.Calculate(); // node and wire position
            return (childNode, childNodeOutput);
        }

        void CreateChildren(Node node, int nodeIdx)
        {
            var data = nodes[nodeIdx];

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
                        var (_, stateNodeOut) = CreateChild<Pose>(node, children.Length, stateInputIdx);
                        nodeGraph.Connect(stateNodeOut, input);
                    }

                    if (entryConditionNodeIdx != -1)
                    {
                        var (_, childOutput) = CreateChild<Value>(node, children.Length, entryConditionNodeIdx, stateName);
                        nodeGraph.Connect(childOutput, input);
                    }
                }
            }
            else if (node.NodeType is "ParameterizedSelector" or "ParameterizedClipSelector")
            {
                var options = data.GetArray<int>("m_optionNodeIndices");

                var parameterNodeIdx = data.GetInt32Property("m_parameterNodeIdx");
                CreateInputAndChild<Value>(node, options.Length + 1, parameterNodeIdx);

                var hasWeightsSet = data.GetProperty<bool>("m_bHasWeightsSet");
                var totalWeight = 0;
                var weights = data.GetArray<uint>("m_optionWeights");

                if (hasWeightsSet)
                {
                    totalWeight = Math.Max(1, weights.Sum(w => (int)w));
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

                    CreateInputAndChild<Pose>(node, options.Length + 1, optionNodeIdx, $"Option {++i} {weightDesc}");
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
                    var (_, optionInput) = CreateInputAndChild<Pose>(node, options.Length, optionNodeIdx, hub: true);
                    var (_, conditionOutput) = CreateChild<Value>(node, options.Length, conditionNodeIdx);
                    nodeGraph.Connect(conditionOutput, optionInput);
                    i++;
                }
            }
            else if (node.NodeType is "LayerBlend")
            {
                var baseNodeIdx = data.GetInt32Property("m_nBaseNodeIdx");
                CreateInputAndChild<Pose>(node, 3, baseNodeIdx, "Base", "Result");

                var layerInput = new SocketIn(typeof(Pose), "Layers", node, true);
                node.Sockets.Add(layerInput);

                var layerDefinition = data.GetArray("m_layerDefinition");
                var layerIndex = 0;
                foreach (var layer in layerDefinition)
                {
                    var layerNode = nodeGraph.AddNode(new Node(layerDefinition[layerIndex])
                    {
                        Name = $"Layer{layerIndex}",
                        NodeType = "_LayerDefinition_",
                    });

                    var layerOutput = new SocketOut(typeof(Pose), string.Empty, layerNode);
                    layerNode.Sockets.Add(layerOutput);
                    nodeGraph.Connect(layerOutput, layerInput);
                    CreateInputAndChild<Pose>(layerNode, 1, layer.GetInt32Property("m_nInputNodeIdx"));

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
                    layerNode.UpdateTypeColorFromOutput();
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
                    CreateInputAndChild<Value>(node, childCount, inputNodeIdx, "Parameter");
                }
                else if (node.NodeType == "Blend2D")
                {
                    var inputNodeIdxA = data.GetInt32Property("m_nInputParameterNodeIdx0");
                    var inputNodeIdxB = data.GetInt32Property("m_nInputParameterNodeIdx1");

                    childCount += 1;
                    CreateInputAndChild<Value>(node, childCount, inputNodeIdxA, "Parameter A");
                    CreateInputAndChild<Value>(node, childCount, inputNodeIdxB, "Parameter B");
                }

                var optionIndex = 0;
                foreach (var sourceNodeIdx in sourceNodeIndices)
                {
                    CreateInputAndChild<Pose>(node, childCount, sourceNodeIdx, $"Option {++optionIndex}");
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
                CreateInputAndChild<Pose>(node, 3, data.GetInt32Property("m_nChildNodeIdx"), "Input");
                CreateInputAndChild<Value>(node, 3, data.GetInt32Property("m_nInputValueNodeIdx"), "Scale Value");
                node.AddText($"Default Scale: {data.GetFloatProperty("m_flDefaultInputValue")}");
            }
            else if (node.NodeType is "Not" or "FloatCurve")
            {
                CreateInputAndChild<Value>(node, 1, data.GetInt32Property("m_nInputValueNodeIdx"), "Value");

                // curve
            }
            else if (node.NodeType is "FloatRemap")
            {
                CreateInputAndChild<Value>(node, 1, data.GetInt32Property("m_nInputValueNodeIdx"), "Value");
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
            else if (node.NodeType.EndsWith("Math", StringComparison.Ordinal))
            {
                var inputNodeIdxA = data.GetProperty("m_nInputValueNodeIdxA", -1);
                var inputNodeIdxB = data.GetProperty("m_nInputValueNodeIdxB", -1);

                CreateInputAndChild<Value>(node, 2, inputNodeIdxA, "A");

                var @operator = data.GetProperty<string>("m_operator");
                node.AddText(@operator);

                if (inputNodeIdxB != -1)
                {
                    CreateInputAndChild<Value>(node, 2, inputNodeIdxB, "B");
                }
                else
                {
                    if (node.NodeType == "FloatMath")
                    {
                        node.AddText($"{data.GetFloatProperty("m_flValueB"):f}");
                    }
                }
            }
            else if (node.NodeType.EndsWith("Comparison", StringComparison.Ordinal))
            {
                var childNodeIdx = data.GetInt32Property("m_nInputValueNodeIdx");
                CreateInputAndChild<Value>(node, 1, childNodeIdx, GetName(childNodeIdx));

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
                        CreateInputAndChild<Value>(node, 1, comparandNodeIdx, "Comparand");
                    }
                    else
                    {
                        node.AddText($"{data.GetFloatProperty("m_flComparisonValue"):f}");
                    }
                }
                else
                {
                    Log.Error(nameof(AnimationGraphViewer), $"Generic handled node: {node.NodeType} ({node.Name})");
                }
            }
            else if (node.Data?.ContainsKey("m_conditionNodeIndices") ?? false) // Conditional node
            {
                var conditions = data.GetArray<int>("m_conditionNodeIndices");
                foreach (var condition in conditions)
                {
                    CreateInputAndChild<Value>(node, conditions.Length + 1, condition);
                }
            }
            else if (node.Data?.ContainsKey("m_nChildNodeIdx") ?? false)
            {
                var childCount = 1;
                if (node.NodeType == "Scale")
                {
                    childCount = 3;
                    CreateInputAndChild<Pose>(node, childCount, data.GetInt32Property("m_nMaskNodeIdx"), "Mask");
                    CreateInputAndChild<Value>(node, childCount, data.GetInt32Property("m_nEnableNodeIdx"), "Enable");
                }
                else if (node.NodeType == "TwoBoneIK")
                {
                    childCount = 2;
                    node.AddText($"Bone: {data.GetProperty<string>("m_effectorBoneID")}");
                    CreateInputAndChild<Pose>(node, childCount, data.GetInt32Property("m_nEffectorTargetNodeIdx"), "Effector");
                    var enabledNodeIdx = data.GetInt32Property("m_nEnabledNodeIdx");
                    if (enabledNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, childCount, enabledNodeIdx, "Enabled");
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
                CreateInputAndChild<Pose>(node, childCount, childNodeIdx, "Input", "Result");
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
                        CreateInputAndChild<Pose>(node, 1, fallbackNodeIdx, "Fallback");
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
                        CreateInputAndChild<Value>(node, 1, playInReverseNodeIdx, "Play in reverse");
                    }

                    var resetTimeValueNodeIdx = data.GetInt32Property("m_nResetTimeValueNodeIdx");
                    if (resetTimeValueNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, 1, resetTimeValueNodeIdx, "Reset time");
                    }

                }
                else if (node.NodeType is "AnimationPose")
                {
                    node.AddSpace();

                    var poseTimeNodeIdx = data.GetInt32Property("m_nPoseTimeValueNodeIdx");
                    if (poseTimeNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, 1, poseTimeNodeIdx, "Time");
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
            else if (node.NodeType.StartsWith("ControlParameter", StringComparison.Ordinal))
            {
                // Graph input value set by game code.
            }
            else
            {
                Log.Error(nameof(AnimationGraphViewer), $"Unhandled node type: {node.NodeType} ({node.Name})");
            }

            node.UpdateTypeColorFromOutput();
            node.Calculate();
        }

        var finalPose = new Node(null)
        {
            Name = "Result",
            NodeType = "FinalPose",
            HeaderColor = PoseColor,
        };

        var finalPoseInput = new SocketIn(typeof(Pose), "Out", finalPose, hub: false);
        finalPose.Sockets.Add(finalPoseInput);
        nodeGraph.AddNode(finalPose);

        var root = CreateNode(nodePaths, nodes, rootNodeIdx);

        var rootOutput = new SocketOut(typeof(Pose), string.Empty, root);
        root.Sockets.Add(rootOutput);
        nodeGraph.Connect(rootOutput, finalPoseInput);

        CreateChildren(root, rootNodeIdx);

        // create some unreferenced nodes
        for (var i = 0; i < nodes.Length; i++)
        {
            var exists = createdNodes.ContainsKey(i);
            if (exists)
            {
                continue;
            }

            var className = GetType(i);

            if (className.StartsWith("ControlParameter", StringComparison.Ordinal))
            {
                CreateNode(nodePaths, nodes, i);
                continue;
            }
        }

        nodeGraph.LayoutNodes();
        Log.Debug(nameof(AnimationGraphViewer), $"Created {createdNodes.Count} nodes (out of {nodes.Length}) or {createdNodes.Count / (float)nodes.Length:P}.");
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

            // Determine which icon to use based on the file extension
            var isGraphFile = ExternalResourceName.Contains(".vnmgraph", StringComparison.OrdinalIgnoreCase);
            var iconName = isGraphFile ? "anmgrph" : "anim";

            // Get icon from cache
            IconCache.TryGetValue(iconName, out var iconToUse);

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
            trimStr = trimStr.Replace(".vnmgraph", string.Empty, StringComparison.Ordinal);
            var lastSlashIndex = trimStr.LastIndexOf('/');
            if (lastSlashIndex >= 0)
            {
                trimStr = trimStr[(lastSlashIndex + 1)..];
            }
            if (trimStr.Length > 23)
            {
                trimStr = '…' + trimStr[^22..];
            }

            using var paint = new SKPaint { Color = PoseColor, IsAntialias = true };
            canvas.DrawText(trimStr, textPosition.X, textPosition.Y, ArialFont, paint);
        }

        private static readonly Dictionary<string, SKSvg> IconCache = [];

        static Node()
        {
            string[] icons =
            [
                "anim",
                "anmgrph",
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
