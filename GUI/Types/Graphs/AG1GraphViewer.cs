using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Graphs;

/// <summary>
/// Graph viewer for compiled AG1 (Animation Graph 1) .vanmgrph files.
/// </summary>
internal class AG1GraphViewer : GLNodeGraphViewer
{
    private readonly KVObject animGraphData;
    private readonly IReadOnlyList<KVObject> compiledNodes;
    private readonly Dictionary<int, Node> nodeMap = new();
    private readonly Dictionary<int, KVObject> parameterIndexToObject = new();
    private readonly Dictionary<int, string> parameterIndexToName = new();
    private readonly Dictionary<string, List<KVObject>> typeToParameters = new();

    // Fixed colors per node type (AG1 specific)
    private static SKColor NodeColor { get; set; } = new SKColor(60, 60, 60);
    private static SKColor NodeTextColor { get; set; } = new SKColor(230, 230, 230);

    //Animation
    private static SKColor SequenceColor { get; set; } = new SKColor(62, 43, 89);
    private static SKColor SingleFrameColor { get; set; } = new SKColor(111, 53, 195);
    private static SKColor ChoiceColor { get; set; } = new SKColor(191, 189, 94);
    private static SKColor SelectorColor { get; set; } = new SKColor(122, 197, 255);
    private static SKColor MotionMatchingColor { get; set; } = new SKColor(127, 145, 218);
    //Blend
    private static SKColor BlendColor { get; set; } = new SKColor(45, 102, 82);
    private static SKColor Blend2DColor { get; set; } = new SKColor(7, 62, 42);
    private static SKColor BoneMaskColor { get; set; } = new SKColor(116, 51, 75);
    private static SKColor AddSubtractColor { get; set; } = new SKColor(126, 60, 55);
    private static SKColor AimLeanMatrixColor { get; set; } = new SKColor(99, 43, 87);
    // Constraints and Procedural
    private static SKColor CycleControlColor { get; set; } = new SKColor(108, 138, 61);
    private static SKColor CycleControlClipColor { get; set; } = new SKColor(87, 52, 127);
    private static SKColor FollowAttachmentColor { get; set; } = new SKColor(0, 161, 143);
    private static SKColor FollowPathColor { get; set; } = new SKColor(27, 61, 82);
    private static SKColor ConstraintLightGreenColor { get; set; } = new SKColor(91, 143, 56);
    private static SKColor JiggleBoneColor { get; set; } = new SKColor(87, 175, 122);
    private static SKColor ConstraintDarkGreenColor { get; set; } = new SKColor(44, 106, 31);
    // System
    private static SKColor ChoreoColor { get; set; } = new SKColor(123, 78, 35);
    private static SKColor SystemYellowColor { get; set; } = new SKColor(78, 73, 29);
    private static SKColor InputStreamColor { get; set; } = new SKColor(166, 0, 207);
    private static SKColor SystemDarkBlueColor { get; set; } = new SKColor(24, 54, 72);
    private static SKColor SystemGrayColor { get; set; } = new SKColor(65, 90, 114);
    private static SKColor SlowDownOnSlopesColor { get; set; } = new SKColor(26, 59, 18);
    private static SKColor SteamVRSkeletalInputColor { get; set; } = new SKColor(15, 101, 144);
    private static SKColor TurnHelperColor { get; set; } = new SKColor(32, 30, 78);
    // Main
    private static SKColor StateMachineColor { get; set; } = new SKColor(39, 91, 156);
    private static SKColor FinalPoseColor { get; set; } = new SKColor(211, 175, 55);
    private static SKColor PoseColor { get; set; } = new SKColor(173, 255, 47);          // Default fallback

    public AG1GraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, CreateAndConfigureNodeGraph(data, out var graphDef))
    {
        animGraphData = graphDef;

        // Load parameters from m_pParamListUpdater
        LoadParameters();

        // Extract nodes from m_pSharedData (or directly from root if present)
        if (animGraphData.ContainsKey("m_pSharedData"))
        {
            var sharedData = animGraphData.GetSubCollection("m_pSharedData");
            if (sharedData.ContainsKey("m_nodes"))
                compiledNodes = sharedData.GetArray("m_nodes");
            else
                compiledNodes = Array.Empty<KVObject>();
        }
        else if (animGraphData.ContainsKey("m_nodes"))
        {
            compiledNodes = animGraphData.GetArray("m_nodes");
        }
        else
        {
            compiledNodes = Array.Empty<KVObject>();
        }

        CreateGraph();
    }

    private void LoadParameters()
    {
        parameterIndexToName.Clear();
        parameterIndexToObject.Clear();
        typeToParameters.Clear();

        KVObject? paramListUpdater = null;

        // First try m_pSharedData.m_pParamListUpdater
        if (animGraphData.ContainsKey("m_pSharedData"))
        {
            var sharedData = animGraphData.GetSubCollection("m_pSharedData");
            if (sharedData.ContainsKey("m_pParamListUpdater"))
                paramListUpdater = sharedData.GetSubCollection("m_pParamListUpdater");
        }

        // Fallback: direct root
        if (paramListUpdater == null && animGraphData.ContainsKey("m_pParamListUpdater"))
            paramListUpdater = animGraphData.GetSubCollection("m_pParamListUpdater");

        if (paramListUpdater == null)
            return;

        if (!paramListUpdater.ContainsKey("m_parameters"))
            return;

        var parameters = paramListUpdater.GetArray("m_parameters");
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            var name = param.GetStringProperty("m_name");
            if (!string.IsNullOrEmpty(name))
            {
                parameterIndexToName[i] = name;
                parameterIndexToObject[i] = param;

                // Also index by type for handle resolution (like AnimationGraphExtract)
                var className = param.GetStringProperty("_class");
                var type = ClassNameToParamType(className);
                if (!typeToParameters.TryGetValue(type, out var list))
                {
                    list = [];
                    typeToParameters[type] = list;
                }
                list.Add(param);
            }
        }
    }

    /// <summary>
    /// Maps C++ class name to parameter type string (e.g., "CFloatAnimParameter" -> "FLOAT").
    /// </summary>
    private static string ClassNameToParamType(string className)
    {
        if (string.IsNullOrEmpty(className))
            return "UNKNOWN";

        return className switch
        {
            "CFloatAnimParameter" => "FLOAT",
            "CEnumAnimParameter" => "ENUM",
            "CBoolAnimParameter" => "BOOL",
            "CIntAnimParameter" => "INT",
            "CVectorAnimParameter" => "VECTOR",
            "CQuaternionAnimParameter" => "QUATERNION",
            "CSymbolAnimParameter" => "SYMBOL",
            "CVirtualAnimParameter" => "VIRTUAL",
            _ => className.Replace("AnimParameter", "").TrimStart('C').ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Resolves a CAnimParamHandle (KVObject with m_type and m_index) to a parameter KVObject.
    /// </summary>
    private KVObject? ResolveParameterHandle(KVObject handle)
    {
        if (handle == null)
            return null;

        var type = handle.GetStringProperty("m_type");
        var index = handle.GetInt32Property("m_index", -1);

        if (string.IsNullOrEmpty(type) || index < 0 || index == 255)
            return null;

        // Remove "ANIMPARAM_" prefix to get type name
        var typeName = type.Replace("ANIMPARAM_", "", StringComparison.Ordinal);
        if (!typeToParameters.TryGetValue(typeName, out var paramList))
            return null;

        if (index < paramList.Count)
            return paramList[index];

        return null;
    }

    /// <summary>
    /// Gets a detailed parameter description from a raw index (global index, for compatibility).
    /// </summary>
    private string GetParameterDescriptionFromIndex(int index)
    {
        if (index < 0 || index == 255)
            return "None";

        if (parameterIndexToObject.TryGetValue(index, out var paramObj))
            return GetParameterDescriptionFromObject(paramObj);
        if (parameterIndexToName.TryGetValue(index, out var name))
            return name;
        return $"Param {index}";
    }

    /// <summary>
    /// Gets a detailed parameter description from a parameter KVObject.
    /// </summary>
    private static string GetParameterDescriptionFromObject(KVObject paramObj)
    {
        var className = paramObj.GetStringProperty("_class");
        var typeName = ClassNameToParamType(className);
        var name = paramObj.GetStringProperty("m_name") ?? "Unnamed";

        var result = $"{name} ({typeName})";

        // For Enum, show options
        if (className == "CEnumAnimParameter" && paramObj.ContainsKey("m_enumOptions"))
        {
            var enumOptions = paramObj.GetArray<string>("m_enumOptions");
            if (enumOptions.Length > 0)
            {
                var optStr = string.Join(", ", enumOptions.Take(4));
                if (enumOptions.Length > 4)
                    optStr += ", ...";
                result += $" [ {optStr} ]";
            }
        }

        // Show default value if available
        if (paramObj.ContainsKey("m_bDefaultValue"))
        {
            var def = paramObj.GetBooleanProperty("m_bDefaultValue");
            result += $" (default: {def})";
        }
        else if (paramObj.ContainsKey("m_fDefaultValue"))
        {
            var def = paramObj.GetFloatProperty("m_fDefaultValue");
            result += $" (default: {def:F2})";
        }
        else if (paramObj.ContainsKey("m_defaultValue"))
        {
            var def = paramObj["m_defaultValue"];
            if (def.ValueType == KVValueType.Int32 || def.ValueType == KVValueType.UInt32)
                result += $" (default: {def.ToInt32()})";
            else if (def.ValueType == KVValueType.Array && def.ToArray().Length > 0)
                result += $" (default: {string.Join(", ", def.ToArray().Select(v => v.ToString()))})";
        }

        return result;
    }

    /// <summary>
    /// Gets the parameter description from a CAnimParamHandle KVObject.
    /// </summary>
    private string GetParameterDescriptionFromHandle(KVObject handle)
    {
        if (handle == null)
            return "?";

        var paramObj = ResolveParameterHandle(handle);
        if (paramObj != null)
        {
            var desc = GetParameterDescriptionFromObject(paramObj);
            var type = handle.GetStringProperty("m_type");
            var index = handle.GetInt32Property("m_index", -1);
            return $"{desc} (handle: {type} idx {index})";
        }

        // Fallback: show raw handle info
        var typeRaw = handle.GetStringProperty("m_type", "Unknown");
        var indexRaw = handle.GetInt32Property("m_index", -1);
        return $"{typeRaw} idx {indexRaw}";
    }

    /// <summary>
    /// Universal parameter display: handles m_paramIndex, m_paramX, m_paramY, m_param, m_hParameter, m_hBlendParameter, m_hParam.
    /// </summary>
    private void DisplayUniversalParameters(KVObject compiledNode, Node node)
    {
        // List of known parameter field names
        string[] paramFields =
        {
            "m_paramIndex",        // Direct integer index
            "m_paramX",            // Blend 2D X axis
            "m_paramY",            // Blend 2D Y axis
            "m_param",             // Generic parameter handle
            "m_hParameter",        // Selector, StanceOverride
            "m_hBlendParameter",   // BoneMask
            "m_hParam",            // FollowPath, etc.
        };

        foreach (string field in paramFields)
        {
            if (!compiledNode.ContainsKey(field))
                continue;

            var value = compiledNode[field];

            // Case 1: Direct integer (m_paramIndex)
            if (value.ValueType == KVValueType.Int32 || value.ValueType == KVValueType.UInt32)
            {
                int index = value.ToInt32();
                // Try to resolve as handle first? In some graphs it's a direct index but we'll treat as global.
                string display = GetParameterDescriptionFromIndex(index);
                node.AddText($"{field}: {display}");
                continue;
            }

            // Case 2: Collection with m_type and m_index (CAnimParamHandle)
            if (value.ValueType == KVValueType.Collection)
            {
                var handle = compiledNode.GetSubCollection(field);
                if (handle != null)
                {
                    string info = GetParameterDescriptionFromHandle(handle);
                    node.AddText($"{field}: {info}");
                }
                continue;
            }

            // Case 3: String (parameter name directly)
            if (value.ValueType == KVValueType.String)
            {
                string name = value.ToString();
                if (!string.IsNullOrEmpty(name))
                    node.AddText($"{field}: {name}");
            }
        }
    }

    /// <summary>
    /// Parses a 2D vector from a KVObject that may be an array or a collection with numeric keys.
    /// </summary>
    private static (float x, float y) ParseVector2(KVObject obj, string key)
    {
        if (!obj.ContainsKey(key))
            return (0f, 0f);

        var value = obj[key];

        // Try as a float array first (most common)
        if (value.ValueType == KVValueType.Array)
        {
            var array = obj.GetFloatArray(key);
            if (array != null && array.Length >= 2)
                return (array[0], array[1]);
        }

        // Fallback: try as a sub-collection with keys "0" and "1"
        if (value.ValueType == KVValueType.Collection)
        {
            var sub = obj.GetSubCollection(key);
            if (sub != null)
            {
                float x = sub.GetFloatProperty("0", 0f);
                float y = sub.GetFloatProperty("1", 0f);
                return (x, y);
            }
        }

        return (0f, 0f);
    }

    private static NodeGraphControl CreateAndConfigureNodeGraph(KVObject data, out KVObject graphDef)
    {
        graphDef = data;
        var nodeGraph = new NodeGraphControl
        {
            GridStyle = NodeGraphControl.EGridStyle.Grid,
            CanvasBackgroundColor = new SKColor(40, 40, 40)
        };

        nodeGraph.GridColor = SKColors.White;

        if (Themer.CurrentTheme == Themer.AppTheme.Dark)
        {
            nodeGraph.CanvasBackgroundColor = ToSKColor(Themer.CurrentThemeColors.AppMiddle);
            NodeColor = ToSKColor(Themer.CurrentThemeColors.AppSoft);
            nodeGraph.GridColor = ToSKColor(Themer.CurrentThemeColors.ContrastSoft);
        }

        // Register type colors for sockets (optional – doesn't affect node header)
        NodeGraphControl.AddTypeColorPair<Pose>(PoseColor);

        return nodeGraph;
    }

    private static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);

    private void CreateGraph()
    {
        if (compiledNodes == null || compiledNodes.Count == 0)
            return;

        // First pass: create all nodes
        for (int i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];
            var node = CreateNode(compiledNode, i);
            nodeMap[i] = node;
        }

        // Second pass: create connections from parent to children
        for (int i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];
            var parentNode = nodeMap[i];
            var className = compiledNode.GetStringProperty("_class");

            // Collect all child node indices
            var childIndices = new HashSet<int>();

            // --- m_children array (Choice, Blend, etc.) ---
            if (compiledNode.ContainsKey("m_children"))
            {
                var children = compiledNode.GetArray("m_children");
                foreach (var child in children)
                {
                    if (child.ValueType == KVValueType.Collection && child.ContainsKey("m_nodeIndex"))
                    {
                        int idx = child.GetInt32Property("m_nodeIndex");
                        if (idx >= 0) childIndices.Add(idx);
                    }
                    else if (child.ValueType == KVValueType.Int32)
                    {
                        int idx = child.ToInt32();
                        if (idx >= 0) childIndices.Add(idx);
                    }
                }
            }

            // --- Single child properties (Unary/Binary) ---
            string[] childProps = { "m_pChildNode", "m_pChild1", "m_pChild2", "m_pChild" };
            foreach (var prop in childProps)
            {
                if (compiledNode.ContainsKey(prop))
                {
                    var childRef = compiledNode[prop];
                    if (childRef.ValueType == KVValueType.Collection && childRef.ContainsKey("m_nodeIndex"))
                    {
                        int idx = childRef.GetInt32Property("m_nodeIndex");
                        if (idx >= 0) childIndices.Add(idx);
                    }
                }
            }

            // --- Blend 2D: items may have m_pChild with a node index ---
            if (className == "CBlend2DUpdateNode" && compiledNode.ContainsKey("m_items"))
            {
                var items = compiledNode.GetArray("m_items");
                foreach (var item in items)
                {
                    if (item.ContainsKey("m_pChild"))
                    {
                        var childRef = item.GetSubCollection("m_pChild");
                        if (childRef.ContainsKey("m_nodeIndex"))
                        {
                            int idx = childRef.GetInt32Property("m_nodeIndex");
                            if (idx >= 0) childIndices.Add(idx);
                        }
                    }
                }
            }

            // --- State machine: children in m_stateData ---
            if (className == "CStateMachineUpdateNode" && compiledNode.ContainsKey("m_stateData"))
            {
                var stateDataArray = compiledNode.GetArray("m_stateData");
                // Get state names for labeling
                var stateNames = new List<string>();
                if (compiledNode.ContainsKey("m_stateMachine"))
                {
                    var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
                    if (stateMachine.ContainsKey("m_states"))
                    {
                        var states = stateMachine.GetArray("m_states");
                        foreach (var state in states)
                            stateNames.Add(state.GetStringProperty("m_name", "Unnamed"));
                    }
                }

                for (int s = 0; s < stateDataArray.Count; s++)
                {
                    var stateData = stateDataArray[s];
                    if (stateData.ContainsKey("m_pChild"))
                    {
                        var childRef = stateData.GetSubCollection("m_pChild");
                        if (childRef.ContainsKey("m_nodeIndex"))
                        {
                            int idx = childRef.GetInt32Property("m_nodeIndex");
                            if (idx >= 0)
                                childIndices.Add(idx);
                        }
                    }
                }
            }

            // --- For each child index, create a connection ---
            foreach (int childIdx in childIndices)
            {
                if (!nodeMap.TryGetValue(childIdx, out var childNode))
                    continue;

                // Create output socket on child if none exists
                if (!childNode.Sockets.OfType<SocketOut>().Any())
                {
                    var outSocket = new SocketOut(typeof(Pose), string.Empty, childNode);
                    childNode.Sockets.Add(outSocket);
                }

                // Label the input socket
                string inputLabel = $"Child {childIdx}";
                if (className == "CStateMachineUpdateNode")
                {
                    // Find the state name for this child
                    if (compiledNode.ContainsKey("m_stateData") && compiledNode.ContainsKey("m_stateMachine"))
                    {
                        var stateDataArray = compiledNode.GetArray("m_stateData");
                        var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
                        if (stateMachine.ContainsKey("m_states"))
                        {
                            var states = stateMachine.GetArray("m_states");
                            for (int s = 0; s < stateDataArray.Count; s++)
                            {
                                var stateData = stateDataArray[s];
                                if (stateData.ContainsKey("m_pChild"))
                                {
                                    var childRef = stateData.GetSubCollection("m_pChild");
                                    if (childRef.ContainsKey("m_nodeIndex") && childRef.GetInt32Property("m_nodeIndex") == childIdx)
                                    {
                                        inputLabel = states[s].GetStringProperty("m_name", $"State {s}");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (className == "CBlend2DUpdateNode" && compiledNode.ContainsKey("m_items"))
                {
                    // Try to label with the sequence name or position
                    var items = compiledNode.GetArray("m_items");
                    for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                    {
                        var item = items[itemIdx];
                        if (item.ContainsKey("m_pChild"))
                        {
                            var childRef = item.GetSubCollection("m_pChild");
                            if (childRef.ContainsKey("m_nodeIndex") && childRef.GetInt32Property("m_nodeIndex") == childIdx)
                            {
                                int seqIdx = item.GetInt32Property("m_hSequence", -1);
                                var pos = ParseVector2(item, "m_vPos");
                                inputLabel = $"Item {itemIdx} (Seq {seqIdx}) ({pos.x:F1}, {pos.y:F1})";
                                break;
                            }
                        }
                    }
                }

                var inputSocket = new SocketIn(typeof(Pose), inputLabel, parentNode, hub: true);
                parentNode.Sockets.Add(inputSocket);

                // Connect child's output to parent's input
                var childOutput = childNode.Sockets.OfType<SocketOut>().First();
                nodeGraph.Connect(childOutput, inputSocket);
            }

            // Recalculate node layout after adding sockets
            parentNode.Calculate();
        }

        // Layout nodes
        nodeGraph.LayoutNodes();
    }

    private Node CreateNode(KVObject compiledNode, int index)
    {
        var className = compiledNode.GetStringProperty("_class");
        string displayName = className?.Replace("UpdateNode", "", StringComparison.Ordinal) ?? "Unknown";

        string nodeName = compiledNode.GetStringProperty("m_name");
        if (string.IsNullOrEmpty(nodeName))
            nodeName = displayName;

        var node = new Node(compiledNode)
        {
            Name = $"({index}) {nodeName}",
            NodeType = displayName,
        };

        // --- Fixed header color based on node type (not context) ---

        //Animation
        if (className == "CSequenceUpdateNode")
            node.HeaderColor = SequenceColor;
        else if (className == "CChoiceUpdateNode")
            node.HeaderColor = ChoiceColor;
        else if (className == "CMotionMatchingUpdateNode")
            node.HeaderColor = MotionMatchingColor;
        else if (className == "CSelectorUpdateNode")
            node.HeaderColor = SelectorColor;
        else if (className == "CSingleFrameUpdateNode")
            node.HeaderColor = SingleFrameColor;
        // Blend
        else if (className == "CDirectionalBlendUpdateNode" || className == "CBlendUpdateNode")
            node.HeaderColor = BlendColor;
        else if (className == "CBlend2DUpdateNode")
            node.HeaderColor = Blend2DColor;
        else if (className == "CAddUpdateNode" || className == "CSubtractUpdateNode")
            node.HeaderColor = AddSubtractColor;
        else if (className == "CAimMatrixUpdateNode" || className == "CLeanMatrixUpdateNode")
            node.HeaderColor = AimLeanMatrixColor;
        else if (className == "CBoneMaskUpdateNode")
            node.HeaderColor = BoneMaskColor;
        // Constraints and Procedural
        else if (className == "CCycleControlUpdateNode")
            node.HeaderColor = CycleControlColor;
        else if (className == "CCycleControlClipUpdateNode")
            node.HeaderColor = CycleControlClipColor;
        else if (className == "CFollowAttachmentUpdateNode")
            node.HeaderColor = FollowAttachmentColor;
        else if (className == "CFollowPathUpdateNode")
            node.HeaderColor = FollowPathColor;
        else if (className == "CFootPinningUpdateNode" || className == "CLookAtUpdateNode" || className == "CHitReactUpdateNode" || className == "CFootLockUpdateNode")
            node.HeaderColor = ConstraintLightGreenColor;
        else if (className == "CJiggleBoneUpdateNode")
            node.HeaderColor = JiggleBoneColor;
        else if (className == "CSolveIKChainUpdateNode" || className == "CTwoBoneIKUpdateNode" || className == "CSpeedScaleUpdateNode")
            node.HeaderColor = ConstraintDarkGreenColor;
        // System
        else if (className == "CChoreoUpdateNode")
            node.HeaderColor = ChoreoColor;
        else if (className == "CDirectPlaybackUpdateNode" || className == "CFootStepTriggerUpdateNode")
            node.HeaderColor = SystemYellowColor;
        else if (className == "CInputStreamUpdateNode")
            node.HeaderColor = InputStreamColor;
        else if (className == "CMoverUpdateNode" || className == "CStopAtGoalUpdateNode")
            node.HeaderColor = SystemDarkBlueColor;
        else if (className == "CPathHelperUpdateNode" || className == "CSetFacingUpdateNode")
            node.HeaderColor = SystemGrayColor;
        else if (className == "CSlowDownOnSlopesUpdateNode")
            node.HeaderColor = SlowDownOnSlopesColor;
        else if (className == "CSkeletalInputUpdateNode")
            node.HeaderColor = SteamVRSkeletalInputColor;
        else if (className == "CTurnHelperUpdateNode")
            node.HeaderColor = TurnHelperColor;
        // Main
        else if (className == "CRootUpdateNode")
            node.HeaderColor = FinalPoseColor;
        else if (className == "CStateMachineUpdateNode")
            node.HeaderColor = StateMachineColor;
        else
            node.HeaderColor = PoseColor; // fallback

        // --- Add key properties as text inside the node ---
        foreach (var kv in compiledNode.Children)
        {
            string key = kv.Key;
            if (key == "_class" || key == "m_nodePath" || key == "m_children" || key.StartsWith("m_pChild") ||
                key == "m_tags" || key == "m_paramSpans" || key == "m_stateMachine" || key == "m_stateData" || key == "m_transitionData")
                continue;

            if (kv.Value.ValueType == KVValueType.Collection || kv.Value.ValueType == KVValueType.Array)
                continue;

            if (className == "CChoiceUpdateNode" && (key == "m_weights" || key == "m_blendTimes"))
                continue;

            string valueStr = kv.Value.ToString();
            if (!string.IsNullOrEmpty(valueStr))
                node.AddText($"{key}: {valueStr}");
        }

        // --- Universal parameter display ---
        DisplayUniversalParameters(compiledNode, node);

        // --- Specific node details (non-parameter) ---

        // Choice node
        if (className == "CChoiceUpdateNode")
        {
            if (compiledNode.ContainsKey("m_weights"))
            {
                var weights = compiledNode.GetFloatArray("m_weights");
                if (weights.Length > 0)
                    node.AddText($"Weights: [{string.Join(", ", weights.Select(w => w.ToString("F2")))}]");
            }
            if (compiledNode.ContainsKey("m_blendTimes"))
            {
                var blendTimes = compiledNode.GetFloatArray("m_blendTimes");
                if (blendTimes.Length > 0)
                    node.AddText($"BlendTimes: [{string.Join(", ", blendTimes.Select(b => b.ToString("F2")))}]");
            }
            if (compiledNode.ContainsKey("m_choiceMethod"))
                node.AddText($"ChoiceMethod: {compiledNode.GetStringProperty("m_choiceMethod")}");
            if (compiledNode.ContainsKey("m_blendMethod"))
                node.AddText($"BlendMethod: {compiledNode.GetStringProperty("m_blendMethod")}");
        }

        // Sequence node
        if (className == "CSequenceUpdateNode")
        {
            if (compiledNode.ContainsKey("m_hSequence"))
                node.AddText($"Sequence Index: {compiledNode.GetInt32Property("m_hSequence")}");
            if (compiledNode.ContainsKey("m_duration"))
                node.AddText($"Duration: {compiledNode.GetFloatProperty("m_duration"):F2}");
            if (compiledNode.ContainsKey("m_playbackSpeed"))
                node.AddText($"Speed: {compiledNode.GetFloatProperty("m_playbackSpeed"):F2}");
            if (compiledNode.ContainsKey("m_bLoop"))
                node.AddText($"Loop: {compiledNode.GetBooleanProperty("m_bLoop")}");
        }

        // Blend 2D node (extra details beyond parameters)
        if (className == "CBlend2DUpdateNode")
        {
            // Show blend source axes (if not already shown by universal parameter display)
            if (compiledNode.ContainsKey("m_blendSourceX"))
                node.AddText($"Blend X: {compiledNode.GetStringProperty("m_blendSourceX")}");
            if (compiledNode.ContainsKey("m_blendSourceY"))
                node.AddText($"Blend Y: {compiledNode.GetStringProperty("m_blendSourceY")}");
            if (compiledNode.ContainsKey("m_eBlendMode"))
                node.AddText($"Mode: {compiledNode.GetStringProperty("m_eBlendMode")}");
            if (compiledNode.ContainsKey("m_bLoop"))
                node.AddText($"Loop: {compiledNode.GetBooleanProperty("m_bLoop")}");
            if (compiledNode.ContainsKey("m_playbackSpeed"))
                node.AddText($"Playback Speed: {compiledNode.GetFloatProperty("m_playbackSpeed"):F2}");

            // Damping info
            if (compiledNode.ContainsKey("m_damping"))
            {
                var damping = compiledNode.GetSubCollection("m_damping");
                if (damping != null)
                {
                    string speedFunc = damping.GetStringProperty("m_speedFunction");
                    float speedScale = damping.GetFloatProperty("m_fSpeedScale");
                    node.AddText($"Damping: {speedFunc} (scale {speedScale:F2})");
                }
            }

            // Show all items with positions (no limit)
            if (compiledNode.ContainsKey("m_items"))
            {
                var items = compiledNode.GetArray("m_items");
                node.AddText($"Items: {items.Count}");
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    int seqIdx = item.GetInt32Property("m_hSequence", -1);
                    var pos = ParseVector2(item, "m_vPos");
                    float dur = item.GetFloatProperty("m_flDuration");
                    node.AddText($"  [{i}] Seq {seqIdx} ({pos.x:F1}, {pos.y:F1}) dur={dur:F2}s");
                }
            }
        }

        // BoneMask node (extra details)
        if (className == "CBoneMaskUpdateNode")
        {
            if (compiledNode.ContainsKey("m_nWeightListIndex"))
                node.AddText($"Weight List Index: {compiledNode.GetInt32Property("m_nWeightListIndex")}");
            if (compiledNode.ContainsKey("m_blendSpace"))
                node.AddText($"Blend Space: {compiledNode.GetStringProperty("m_blendSpace")}");
            if (compiledNode.ContainsKey("m_flRootMotionBlend"))
                node.AddText($"Root Motion Blend: {compiledNode.GetFloatProperty("m_flRootMotionBlend"):F2}");
            if (compiledNode.ContainsKey("m_bUseBlendScale"))
                node.AddText($"Use Blend Scale: {compiledNode.GetBooleanProperty("m_bUseBlendScale")}");
        }

        // State machine node
        if (className == "CStateMachineUpdateNode")
        {
            if (compiledNode.ContainsKey("m_stateMachine"))
            {
                var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
                if (stateMachine.ContainsKey("m_states"))
                {
                    var states = stateMachine.GetArray("m_states");
                    node.AddText($"States: {states.Count}");
                    foreach (var state in states)
                    {
                        string name = state.GetStringProperty("m_name", "Unnamed");
                        bool isStart = state.GetIntegerProperty("m_bIsStartState") > 0;
                        node.AddText($"  {(isStart ? "*" : " ")} {name}");
                    }
                }
                if (stateMachine.ContainsKey("m_transitions"))
                    node.AddText($"Transitions: {stateMachine.GetArray("m_transitions").Count}");
            }
            if (compiledNode.ContainsKey("m_transitionData") && compiledNode.GetArray("m_transitionData").Count > 0)
            {
                var first = compiledNode.GetArray("m_transitionData")[0];
                if (first.ContainsKey("m_blendDuration"))
                {
                    var blend = first.GetSubCollection("m_blendDuration");
                    node.AddText($"Blend Duration (first): {blend.GetFloatProperty("m_constValue"):F2}");
                }
            }
        }

        // Root node
        if (className == "CRootUpdateNode" && compiledNode.ContainsKey("m_networkMode"))
            node.AddText($"NetworkMode: {compiledNode.GetStringProperty("m_networkMode")}");

        nodeGraph.AddNode(node);
        return node;
    }

    #region Node class

    private class Node : AbstractNode
    {
        public KVObject? Data { get; set; }

        public Node(KVObject? data)
        {
            Data = data;
            BaseColor = NodeColor;
            TextColor = NodeTextColor;
            HeaderTextColor = new SKColor(5, 5, 5);
            HeaderTypeColor = new SKColor(25, 25, 25);
            // HeaderColor will be set in CreateNode
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
    }

    #endregion

    // Dummy type for socket coloring (optional)
    private struct Pose { }
}
