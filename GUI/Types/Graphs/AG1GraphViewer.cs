using System.Linq;
using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;
using Node = GUI.Types.Graphs.KVGraphNode;

namespace GUI.Types.Graphs;

/// <summary>
/// Graph viewer for compiled AG1 (Animgraph1) files.
/// </summary>
internal class AG1GraphViewer : GLGraphViewer
{
    private readonly KVObject animGraphData;
    private IReadOnlyList<KVObject> compiledNodes = Array.Empty<KVObject>();
    private readonly Dictionary<int, Node> nodeMap = new();
    private readonly Dictionary<int, KVObject> parameterIndexToObject = new();
    private readonly Dictionary<KVObject, List<Node>> parameterConsumers = new();
    private readonly Dictionary<int, string> parameterIndexToName = new();
    private readonly Dictionary<KVObject, int> parameterObjectToIndex = new();
    private readonly Dictionary<string, List<KVObject>> typeToParameters = new();
    private List<KVObject> tags = new();
    private Dictionary<int, string> tagIndexToName = new();
    private List<KVObject> components = new();

    private readonly VrfGuiContext fileLoader;
    private readonly AnimGraphModelInfo modelInfo;
    private Resource? modelResource;
    private bool modelResourceLoaded;

    // Hue slots per node type (AG1 specific)
    private const GraphHue PoseHue = GraphHue.Green;

    // A subgraph node stands for a whole referenced file, so it takes the reserved reference hue
    // rather than any category or data type colour.
    private static readonly GraphHue SubGraphHue = AnimGraphHues.HueOf(AnimGraphCategory.ExternalReference);

    private static readonly Dictionary<string, GraphHue> TagClassHues = new(StringComparer.Ordinal)
    {
        ["CAudioAnimTag"] = GraphHue.Orange,
        ["CBodyGroupAnimTag"] = GraphHue.Green,
        ["CClothSettingsAnimTag"] = GraphHue.Neutral,
        ["CFootFallAnimTag"] = GraphHue.Green,
        ["CFootstepLandedAnimTag"] = GraphHue.Olive,
        ["CMaterialAttributeAnimTag"] = GraphHue.Cyan,
        ["CParticleAnimTag"] = GraphHue.Purple,
        ["CRagdollAnimTag"] = GraphHue.Amber,
        ["CSequenceFinishedAnimTag"] = GraphHue.Indigo,
        ["CStringAnimTag"] = GraphHue.Magenta,
        ["CTaskStatusAnimTag"] = GraphHue.Teal,
        ["CWarpSectionAnimTag"] = GraphHue.Olive,
        ["CMovementHandshakeAnimTag"] = GraphHue.Teal,
        ["CTaskHandshakeAnimTag"] = GraphHue.Teal,
    };

    private static readonly Dictionary<string, GraphHue> ComponentClassHues = new(StringComparer.Ordinal)
    {
        ["CActionComponentUpdater"] = GraphHue.Orange,
        ["CAnimScriptComponentUpdater"] = GraphHue.Slate,
        ["CCPPScriptComponentUpdater"] = GraphHue.Purple,
        ["CDampedValueComponentUpdater"] = GraphHue.Green,
        ["CDemoSettingsComponentUpdater"] = GraphHue.Orange,
        ["CLODComponentUpdater"] = GraphHue.Neutral,
        ["CLookComponentUpdater"] = GraphHue.Cyan,
        ["CMovementComponentUpdater"] = GraphHue.Maroon,
        ["CPairedSequenceComponentUpdater"] = GraphHue.Indigo,
        ["CRagdollComponentUpdater"] = GraphHue.Maroon,
        ["CRemapValueComponentUpdater"] = GraphHue.Green,
        ["CSlopeComponentUpdater"] = GraphHue.Olive,
        ["CStateMachineComponentUpdater"] = GraphHue.Slate,
    };

    private static GraphHue GetComponentClassHue(string className)
    {
        return ComponentClassHues.TryGetValue(className, out var hue) ? hue : GraphHue.Neutral;
    }

    private static GraphHue GetTagClassHue(string className)
    {
        return TagClassHues.TryGetValue(className, out var hue) ? hue : GraphHue.Teal;
    }

    // ---- Dictionaries for class-based lookups ----
    private static readonly Dictionary<string, string> ClassDisplayName = new(StringComparer.Ordinal)
    {
        ["CSequenceUpdateNode"] = "Sequence",
        ["CChoiceUpdateNode"] = "Choice",
        ["CMotionMatchingUpdateNode"] = "Motion Matching",
        ["CSelectorUpdateNode"] = "Selector",
        ["CSingleFrameUpdateNode"] = "Single Frame",
        ["CDirectionalBlendUpdateNode"] = "Directional Blend",
        ["CBlendUpdateNode"] = "Blend",
        ["CBlend2DUpdateNode"] = "Blend 2D",
        ["CAddUpdateNode"] = "Add",
        ["CSubtractUpdateNode"] = "Subtract",
        ["CAimMatrixUpdateNode"] = "Aim Matrix",
        ["CLeanMatrixUpdateNode"] = "Lean Matrix",
        ["CBoneMaskUpdateNode"] = "Bone Mask",
        ["CCycleControlUpdateNode"] = "Cycle Control",
        ["CCycleControlClipUpdateNode"] = "Cycle Control Clip",
        ["CFollowAttachmentUpdateNode"] = "Follow Attachment",
        ["CFollowPathUpdateNode"] = "Follow Path",
        ["CFootPinningUpdateNode"] = "Foot Pinning",
        ["CLookAtUpdateNode"] = "Look At",
        ["CHitReactUpdateNode"] = "Hit React",
        ["CFootLockUpdateNode"] = "Foot Lock",
        ["CJiggleBoneUpdateNode"] = "Jiggle Bone",
        ["CSolveIKChainUpdateNode"] = "Solve IK Chain",
        ["CTwoBoneIKUpdateNode"] = "Two Bone IK",
        ["CSpeedScaleUpdateNode"] = "Speed Scale",
        ["CChoreoUpdateNode"] = "Choreo",
        ["CDirectPlaybackUpdateNode"] = "Direct Playback",
        ["CFootStepTriggerUpdateNode"] = "Foot Step Trigger",
        ["CInputStreamUpdateNode"] = "Input Stream",
        ["CMoverUpdateNode"] = "Mover",
        ["CStopAtGoalUpdateNode"] = "Stop At Goal",
        ["CPathHelperUpdateNode"] = "Path Helper",
        ["CSetFacingUpdateNode"] = "Set Facing",
        ["CSlowDownOnSlopesUpdateNode"] = "Slow Down On Slopes",
        ["CSkeletalInputUpdateNode"] = "Skeletal Input",
        ["CTurnHelperUpdateNode"] = "Turn Helper",
        ["CRootUpdateNode"] = "Final Pose",
        ["CStateMachineUpdateNode"] = "State Machine",
    };

    private static readonly Dictionary<string, string> ComponentDisplayName = new(StringComparer.Ordinal)
    {
        ["CActionComponentUpdater"] = "Action Component",
        ["CAnimScriptComponentUpdater"] = "AnimScript Component",
        ["CCPPScriptComponentUpdater"] = "PPScript Component",
        ["CDampedValueComponentUpdater"] = "Damped Value Component",
        ["CDemoSettingsComponentUpdater"] = "Demo Settings Component",
        ["CLODComponentUpdater"] = "LOD Component",
        ["CLookComponentUpdater"] = "Look Component",
        ["CMovementComponentUpdater"] = "Movement Component",
        ["CPairedSequenceComponentUpdater"] = "Paired Sequence Component",
        ["CRagdollComponentUpdater"] = "Ragdoll Component",
        ["CRemapValueComponentUpdater"] = "Remap Value Component",
        ["CSlopeComponentUpdater"] = "Slope Component",
        ["CStateMachineComponentUpdater"] = "State Machine Component",
    };

    private static readonly Dictionary<string, string> TagDisplayName = new(StringComparer.Ordinal)
    {
        ["CAudioAnimTag"] = "Audio Tag",
        ["CBodyGroupAnimTag"] = "Body Group Tag",
        ["CClothSettingsAnimTag"] = "Cloth Settings Tag",
        ["CFootFallAnimTag"] = "FootFall Tag",
        ["CFootstepLandedAnimTag"] = "FootstepLanded Tag",
        ["CMaterialAttributeAnimTag"] = "Material Attribute Tag",
        ["CParticleAnimTag"] = "Particle Tag",
        ["CRagdollAnimTag"] = "Ragdoll Tag",
        ["CSequenceFinishedAnimTag"] = "Sequence Finished Tag",
        ["CStringAnimTag"] = "String/Internal Tag",
        ["CTaskStatusAnimTag"] = "Status Tag",
        ["CWarpSectionAnimTag"] = "Warp Section Tag",
        ["CMovementHandshakeAnimTag"] = "Movement Handshake Tag",
        ["CTaskHandshakeAnimTag"] = "Task Handshake Tag",
    };

    private static readonly Dictionary<string, string> ParameterTypeDisplayName = new(StringComparer.Ordinal)
    {
        ["BOOL"] = "Boolean",
        ["INT"] = "Integer",
        ["FLOAT"] = "Float",
        ["ENUM"] = "Enum",
        ["VECTOR"] = "Vector",
        ["QUATERNION"] = "Quaternion",
        ["SYMBOL"] = "Symbol",
        ["VIRTUAL"] = "Virtual",
        ["UNKNOWN"] = "Unknown",
    };

    private static GraphHue HueOfClass(string compiledClass)
        => AnimGraphHues.HueOf(AnimGraphHues.CategoryOfAG1(compiledClass));
    private static readonly HashSet<string> GlobalPropertySkips = new(StringComparer.Ordinal)
    {
        "m_name",
    };

    private static readonly Dictionary<string, string> PropertyDisplayNames = new(StringComparer.Ordinal)
    {
        ["m_networkMode"] = "NetworkMode",
    };

    // ---- Properties to skip in generic display for specific classes ----
    private static readonly Dictionary<string, HashSet<string>> ClassPropertySkips = new(StringComparer.Ordinal)
    {
        ["CChoiceUpdateNode"] = new(StringComparer.Ordinal) { "m_weights", "m_blendTimes", "m_choiceMethod", "m_blendMethod", "m_choiceChangeMethod", "m_bCrossFade", "m_bResetChosen", "m_bDontResetSameSelection" },
        ["CSelectorUpdateNode"] = new(StringComparer.Ordinal) { "m_hParameter", "m_param" },
        ["CSequenceUpdateNode"] = new(StringComparer.Ordinal) { "m_hSequence", "m_duration", "m_playbackSpeed", "m_bLoop" },
        ["CBlend2DUpdateNode"] = new(StringComparer.Ordinal) { "m_blendSourceX", "m_blendSourceY", "m_eBlendMode", "m_bLoop", "m_playbackSpeed", "m_damping", "m_items" },
        ["CBoneMaskUpdateNode"] = new(StringComparer.Ordinal) { "m_nWeightListIndex", "m_blendSpace", "m_flRootMotionBlend", "m_bUseBlendScale" },
        ["CStateMachineUpdateNode"] = new(StringComparer.Ordinal) { "m_stateMachine", "m_transitionData" },
        ["CAimMatrixUpdateNode"] = new(StringComparer.Ordinal) { "m_opFixedSettings", "m_target", "m_paramIndex", "m_hSequence", "m_bResetChild", "m_bLockWhenWaning" },
        ["CFollowAttachmentUpdateNode"] = new(StringComparer.Ordinal) { "m_opFixedData" },
        ["CFootPinningUpdateNode"] = new(StringComparer.Ordinal) { "m_poseOpFixedData", "m_eTimingSource", "m_params", "m_bResetChild" },
    };

    private static readonly string[] ChildProperties = { "m_pChildNode", "m_pChild1", "m_pChild2", "m_pChild" };

    public AG1GraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, new GraphView())
    {
        fileLoader = vrfGuiContext;
        animGraphData = data;
        modelInfo = new AnimGraphModelInfo(vrfGuiContext, LoadModel);

        BuildGraph();
    }

    // Parameter feeds fan out from one hub per type into most of the graph, so by default they
    // stay off the canvas: consumers name their parameters as card text and the hub cards list
    // every parameter. The sidebar checkbox rebuilds with the dashed wires drawn.
    private bool drawParameterWires;

    protected override bool HasParameterWireToggle => parameterConsumers.Count > 0;

    protected override void SetDrawParameterWires(bool draw)
    {
        if (drawParameterWires == draw)
        {
            return;
        }

        drawParameterWires = draw;
        View.Rebuild(BuildGraph);
        RefreshStatsLabel();
        RefitToGraph();
    }

    private void BuildGraph()
    {
        nodeMap.Clear();
        parameterConsumers.Clear();
        parameterObjectToIndex.Clear();

        if (IsEditorGraph(animGraphData))
        {
            // HLA and SteamVR Home ship standalone vanmgrph files in the editor format, and CS2
            // authors its player graphs the same way in vanmgrph/vsubgrph. The graph is laid out
            // from its wires like the compiled path; the authored canvas positions are ignored.
            BuildEditorGraph();
            View.LayoutOptions.Features |= GraphLayoutFeature.LongWireDummies;
            View.LayoutOptions.TightenMinSpan = 1;
            View.LayoutNodesPacked();

            View.Legend.AddRange(AnimGraphHues.Legend());
            return;
        }

        LoadParameters();
        LoadTags();
        LoadComponents();

        var nodesContainer = ResolveContainer("m_nodes");
        compiledNodes = nodesContainer?.GetArray("m_nodes") ?? Array.Empty<KVObject>();

        CreateGraph();
        AddParameterAndTagNodes();
        AddComponentNodes();
        // A pose graph is one connected DAG, where routing a long wire through dummy ranks keeps
        // it between the cards of the ranks it spans rather than over them, and where closing
        // every rank of slack pays off instead of costing the room the repair moves cards in.
        View.LayoutOptions.Features |= GraphLayoutFeature.LongWireDummies;
        View.LayoutOptions.TightenMinSpan = 1;
        View.LayoutNodesPacked();

        View.Legend.AddRange(AnimGraphHues.Legend());

        // Always listed, wires drawn or not, so toggling never resizes the legend.
        View.Legend.AddRange(
        [
            new("Parameter link", GraphHue.Olive, GraphLegendKind.DashedWire),
            new("Tag group", GraphHue.Teal),
            new("Component", GraphHue.Neutral),
            new("Client-simulated", GraphHue.Purple, GraphLegendKind.Marker),
        ]);
    }

    // animgraph1 names a child by id, animgraph19 by connection; a node carries one vocabulary
    // or the other, so both are tried against every node.
    private static readonly (string Field, string Label)[] EditorChildFields =
    [
        ("m_childID", "child"),
        ("m_baseChildID", "base"),
        ("m_additiveChildID", "additive"),
        ("m_subtractChildID", "subtract"),
        ("m_child1ID", "child 1"),
        ("m_child2ID", "child 2"),
        ("m_inputConnection", "input"),
        ("m_baseInput", "base"),
        ("m_additiveInput", "additive"),
        ("m_baseInputConnection", "base"),
        ("m_subtractInputConnection", "subtract"),
        ("m_inputConnection1", "input 1"),
        ("m_inputConnection2", "input 2"),
    ];

    private static readonly HashSet<string> EditorSkippedDetailFields = new(StringComparer.Ordinal)
    {
        "_class", "m_sName", "m_vecPosition", "m_nNodeID", "m_networkMode", "m_subGraphFilename",
    };

    /// <summary>
    /// Adds every node of a container to the view, recursing into the node manager a group
    /// carries so the whole authored tree lands on one canvas, and records the group path and
    /// the named outputs the connections in the container address.
    /// </summary>
    private void AddEditorNodes(
        IReadOnlyList<KVObject> nodePairs,
        string? groupPath,
        Dictionary<long, Node> editorNodes,
        Dictionary<long, KVObject> editorData,
        Dictionary<(long Node, long Output), string> outputNames,
        HashSet<string> usedGroupPaths)
    {
        foreach (var pair in nodePairs)
        {
            var key = pair.GetSubCollection("key");
            var value = pair.GetSubCollection("value");

            if (key == null || value == null)
            {
                continue;
            }

            var id = key.GetIntegerProperty("m_id");

            if (editorNodes.ContainsKey(id))
            {
                continue;
            }

            var className = value.GetStringProperty("_class") ?? "Unknown";
            var compiledClass = className.Replace("AnimNode", "UpdateNode", StringComparison.Ordinal);
            var friendly = ClassDisplayName.TryGetValue(compiledClass, out var display) ? display : className;
            var authoredName = value.GetStringProperty("m_sName");

            var node = new Node(value)
            {
                Name = string.IsNullOrEmpty(authoredName) || authoredName == "Unnamed" ? friendly : authoredName,
                NodeType = friendly,
                Category = HueOfClass(compiledClass),
                GroupPath = groupPath,
            };

            // A subgraph node names the file it stands for; the shared resource row shows the
            // graph icon and the reference opens on double click.
            var subGraphFileName = value.GetStringProperty("m_subGraphFilename");

            if (!string.IsNullOrEmpty(subGraphFileName))
            {
                node.Category = SubGraphHue;
                node.AddResourceReference(subGraphFileName, "anmgrph", SubGraphHue);
            }

            if (value.ContainsKey("m_proxyItems"))
            {
                foreach (var proxy in value.GetArray("m_proxyItems"))
                {
                    var proxyName = proxy.GetStringProperty("m_name");

                    if (!string.IsNullOrEmpty(proxyName) && proxy.GetSubCollection("m_outputID") is { } outputRef)
                    {
                        outputNames[(id, outputRef.GetIntegerProperty("m_id"))] = proxyName;
                    }
                }
            }

            AddEditorDetailRows(node, value);
            View.AddNode(node);
            editorNodes[id] = node;
            editorData[id] = value;

            if (ResolveNestedEditorNodes(value) is not { Count: > 0 } childNodes)
            {
                continue;
            }

            // Two groups may share a name; their coordinate systems are still independent, so
            // the paths are kept distinct to stop the layout from merging them.
            var childPath = string.IsNullOrEmpty(groupPath) ? node.Title : $"{groupPath}/{node.Title}";

            for (var suffix = 2; !usedGroupPaths.Add(childPath); suffix++)
            {
                childPath = string.IsNullOrEmpty(groupPath) ? $"{node.Title} #{suffix}" : $"{groupPath}/{node.Title} #{suffix}";
            }

            AddEditorNodes(childNodes, childPath, editorNodes, editorData, outputNames, usedGroupPaths);
        }
    }

    /// <summary>Root classes of the two uncompiled animation graph schemas.</summary>
    private static bool IsEditorGraph(KVObject data)
    {
        var className = data.GetStringProperty("_class");
        return className is "CAnimationGraph" or "CAnimationSubGraph";
    }

    /// <summary>An unset node or output reference.</summary>
    private const long InvalidEditorId = 0xFFFFFFFF;

    /// <summary>
    /// Returns the node list a group or subgraph nests in its own node manager, or null for a
    /// node that holds no nested graph.
    /// </summary>
    private static IReadOnlyList<KVObject>? ResolveNestedEditorNodes(KVObject node)
    {
        foreach (var managerField in new[] { "m_nodeManager", "m_nodeMgr" })
        {
            if (node.GetSubCollection(managerField) is { } manager && manager.ContainsKey("m_nodes"))
            {
                return manager.GetArray("m_nodes");
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the node list of the graph root. animgraph1 keeps it at the root itself,
    /// animgraph19 moves it under a node manager.
    /// </summary>
    private static IReadOnlyList<KVObject>? ResolveRootEditorNodes(KVObject root)
    {
        if (root.ContainsKey("m_nodes"))
        {
            return root.GetArray("m_nodes");
        }

        return ResolveNestedEditorNodes(root);
    }

    // Builds the graph straight from the uncompiled editor schema. Nodes are a key/value list
    // of id to node. animgraph1 (HLA, SteamVR Home) wires children by plain id fields, while
    // animgraph19 (CS2) wires them through m_inputConnection { m_nodeID, m_outputID } and nests
    // groups in a node manager of their own; both are read here and flattened onto one canvas,
    // with each node stamped with the group path it was authored in.
    private void BuildEditorGraph()
    {
        var rootNodes = ResolveRootEditorNodes(animGraphData);

        if (rootNodes == null || rootNodes.Count == 0)
        {
            View.AddNode(new Node(null)
            {
                Name = "Empty animation graph",
                NodeType = animGraphData.GetStringProperty("_class") ?? "CAnimationGraph",
            });
            return;
        }

        var paramNames = new Dictionary<long, string>();

        foreach (var parameters in new[]
        {
            animGraphData.GetSubCollection("m_pParameterList")?.GetArray("m_Parameters"),
            animGraphData.ContainsKey("m_localParameters") ? animGraphData.GetArray("m_localParameters") : null,
        })
        {
            if (parameters == null)
            {
                continue;
            }

            foreach (var parameter in parameters)
            {
                if (parameter.GetSubCollection("m_id") is { } paramId)
                {
                    paramNames[paramId.GetIntegerProperty("m_id")] = parameter.GetStringProperty("m_name") ?? "?";
                }
            }
        }

        var editorNodes = new Dictionary<long, Node>();
        var editorData = new Dictionary<long, KVObject>();

        // Names an output socket that a connection addresses by id, so a group boundary card
        // shows one labelled output per proxy instead of a single anonymous one.
        var outputNames = new Dictionary<(long Node, long Output), string>();
        var usedGroupPaths = new HashSet<string>(StringComparer.Ordinal);

        AddEditorNodes(rootNodes, groupPath: null, editorNodes, editorData, outputNames, usedGroupPaths);

        foreach (var (id, value) in editorData)
        {
            var parent = editorNodes[id];

            // animgraph19 addresses a source by node and output id; animgraph1 names the node
            // alone, in which case the node's single anonymous output is used.
            void ConnectSource(long sourceId, long outputId, string inputName)
            {
                if (sourceId == InvalidEditorId || !editorNodes.TryGetValue(sourceId, out var source) || source == parent)
                {
                    return;
                }

                var outputName = outputNames.GetValueOrDefault((sourceId, outputId), string.Empty);
                var output = source.Outputs.Find(o => o.Name == outputName) ?? source.AddOutput(outputName, PoseHue);
                var input = parent.AddInput(inputName, PoseHue, allowMultiple: true);

                if (!input.Wires.Exists(w => w.From == output))
                {
                    View.Connect(output, input);
                }
            }

            void ConnectConnection(KVObject? connection, string inputName)
            {
                if (connection?.GetSubCollection("m_nodeID") is not { } sourceRef)
                {
                    return;
                }

                var outputId = connection.GetSubCollection("m_outputID")?.GetIntegerProperty("m_id") ?? InvalidEditorId;
                ConnectSource(sourceRef.GetIntegerProperty("m_id"), outputId, inputName);
            }

            foreach (var (field, label) in EditorChildFields)
            {
                if (value.GetSubCollection(field) is not { } childRef)
                {
                    continue;
                }

                if (childRef.ContainsKey("m_nodeID"))
                {
                    ConnectConnection(childRef, label);
                }
                else
                {
                    ConnectSource(childRef.GetIntegerProperty("m_id"), InvalidEditorId, label);
                }
            }

            if (value.ContainsKey("m_children"))
            {
                foreach (var childEntry in value.GetArray("m_children"))
                {
                    var childName = childEntry.GetStringProperty("m_name");
                    var label = string.IsNullOrEmpty(childName) ? "child" : childName;

                    // A child is either a bare connection or a wrapper carrying one alongside
                    // its blend value or selection label.
                    ConnectConnection(childEntry.ContainsKey("m_nodeID") ? childEntry : childEntry.GetSubCollection("m_inputConnection"), label);
                }
            }

            // Group and subgraph boundary nodes republish interior results under named outputs.
            if (value.ContainsKey("m_proxyItems"))
            {
                foreach (var proxy in value.GetArray("m_proxyItems"))
                {
                    var proxyName = proxy.GetStringProperty("m_name");
                    ConnectConnection(proxy.GetSubCollection("m_inputConnection"), string.IsNullOrEmpty(proxyName) ? "proxy" : proxyName);
                }
            }

            // A group card takes the result of its own interior output node, which keeps the
            // flattened graph connected across the group boundary.
            if (value.GetSubCollection("m_outputNodeID") is { } interiorOutput)
            {
                ConnectSource(interiorOutput.GetIntegerProperty("m_id"), InvalidEditorId, "group output");
            }

            if (value.ContainsKey("m_states"))
            {
                ConnectEditorStates(value.GetArray("m_states"), editorNodes, paramNames, ConnectConnection, ConnectSource);
            }
        }
    }

    /// <summary>
    /// Wires a state machine's states to their pose subtrees and draws the dashed state to
    /// state transitions between those subtrees, labeled by their first condition parameter.
    /// </summary>
    private void ConnectEditorStates(
        IReadOnlyList<KVObject> states,
        Dictionary<long, Node> editorNodes,
        Dictionary<long, string> paramNames,
        Action<KVObject?, string> connectConnection,
        Action<long, long, string> connectSource)
    {
        // The card a state's transitions attach to is the root of the state's own subtree.
        var stateChildren = new Dictionary<long, long>();

        foreach (var state in states)
        {
            var stateName = state.GetStringProperty("m_name") ?? "state";
            var label = state.GetBooleanProperty("m_bIsStartState") ? $"{stateName} (start)" : stateName;
            var stateId = state.GetSubCollection("m_stateID")?.GetIntegerProperty("m_id");

            long childId;

            if (state.GetSubCollection("m_inputConnection") is { } connection)
            {
                connectConnection(connection, label);
                childId = connection.GetSubCollection("m_nodeID")?.GetIntegerProperty("m_id") ?? InvalidEditorId;
            }
            else if (state.GetSubCollection("m_childNodeID") is { } childRef)
            {
                childId = childRef.GetIntegerProperty("m_id");
                connectSource(childId, InvalidEditorId, label);
            }
            else
            {
                continue;
            }

            if (stateId is { } id && childId != InvalidEditorId)
            {
                stateChildren[id] = childId;
            }
        }

        foreach (var state in states)
        {
            if (state.GetSubCollection("m_stateID")?.GetIntegerProperty("m_id") is not { } src ||
                !stateChildren.TryGetValue(src, out var srcChildId) ||
                !editorNodes.TryGetValue(srcChildId, out var srcNode) ||
                !state.ContainsKey("m_transitions"))
            {
                continue;
            }

            foreach (var transition in state.GetArray("m_transitions"))
            {
                if (transition.GetSubCollection("m_destState")?.GetIntegerProperty("m_id") is not { } dest ||
                    !stateChildren.TryGetValue(dest, out var destChildId) ||
                    !editorNodes.TryGetValue(destChildId, out var destNode) ||
                    destNode == srcNode)
                {
                    continue;
                }

                // animgraph19 wraps the conditions in a container; animgraph1 lists them inline.
                var conditions = transition.GetSubCollection("m_conditionList")?.GetArray("m_conditions")
                    ?? (transition.ContainsKey("m_conditions") ? transition.GetArray("m_conditions") : null);

                string? label = null;

                if (conditions is { Count: > 0 } && conditions[0].GetSubCollection("m_paramID") is { } conditionParam)
                {
                    label = paramNames.GetValueOrDefault(conditionParam.GetIntegerProperty("m_id"));
                }

                var from = srcNode.Outputs.Find(static o => o.Name == "Transitions") ?? srcNode.AddOutput("Transitions", GraphHue.Slate);
                var to = destNode.Inputs.Find(static i => i.Name == "From") ?? destNode.AddInput("From", GraphHue.Slate, allowMultiple: true);

                if (!to.Wires.Exists(w => w.From == from))
                {
                    View.Connect(from, to, dashed: true, label: label);
                }
            }
        }
    }

    private static void AddEditorDetailRows(Node node, KVObject value)
    {
        var rows = 0;

        foreach (var (name, child) in value)
        {
            if (EditorSkippedDetailFields.Contains(name) || child.ValueType is KVValueType.Collection or KVValueType.Array)
            {
                continue;
            }

            node.AddText($"{name}: {KVGraphNode.StringifyValue(child)}");

            if (++rows >= 8)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Returns the container (shared data if present, otherwise the graph root) that holds the given key.
    /// </summary>
    private KVObject? ResolveContainer(string key)
    {
        if (animGraphData.ContainsKey("m_pSharedData"))
        {
            var sharedData = animGraphData.GetSubCollection("m_pSharedData");
            if (sharedData.ContainsKey(key))
                return sharedData;
        }

        return animGraphData.ContainsKey(key) ? animGraphData : null;
    }

    private void LoadParameters()
    {
        parameterIndexToName.Clear();
        parameterIndexToObject.Clear();
        typeToParameters.Clear();

        var paramListUpdater = ResolveContainer("m_pParamListUpdater")?.GetSubCollection("m_pParamListUpdater");
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
                parameterObjectToIndex[param] = i;

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

    private void LoadTags()
    {
        tags.Clear();
        tagIndexToName.Clear();

        var tagManager = ResolveContainer("m_pTagManagerUpdater")?.GetSubCollection("m_pTagManagerUpdater");
        if (tagManager == null)
            return;

        if (!tagManager.ContainsKey("m_tags"))
            return;

        var tagList = tagManager.GetArray("m_tags");
        for (int i = 0; i < tagList.Count; i++)
        {
            var tagObj = tagList[i];
            tags.Add(tagObj);
            var name = tagObj.GetStringProperty("m_name");
            if (!string.IsNullOrEmpty(name))
            {
                tagIndexToName[i] = name;
            }
        }
    }

    private string GetTagName(int tagIndex)
    {
        if (tagIndex < 0 || tagIndex >= tags.Count)
            return $"Tag {tagIndex}";
        return tagIndexToName.TryGetValue(tagIndex, out var name) ? name : $"Tag {tagIndex}";
    }

    private void LoadComponents()
    {
        components.Clear();

        var componentUpdaters = ResolveContainer("m_components");
        if (componentUpdaters == null)
            return;

        components.AddRange(componentUpdaters.GetArray("m_components"));
    }

    private Resource? LoadModel()
    {
        if (modelResourceLoaded)
            return modelResource;
        modelResourceLoaded = true;

        var modelName = animGraphData.GetStringProperty("m_modelName");
        if (string.IsNullOrEmpty(modelName))
            return null;

        modelResource = fileLoader.LoadFileCompiled(modelName);
        return modelResource;
    }

    private string GetSequenceName(int index) => modelInfo.GetSequenceName(index);

    private string GetWeightListName(int index) => modelInfo.GetWeightListName(index);

    private string GetBoneName(int index)
    {
        var name = modelInfo.GetBoneName(index);
        return string.IsNullOrEmpty(name) ? $"bone_{index}" : name;
    }

    private string GetIKChainName(int index)
    {
        var name = modelInfo.GetIKChainName(index);
        return string.IsNullOrEmpty(name) ? $"ikchain_{index}" : name;
    }

    private string GetFootName(int index)
    {
        var name = modelInfo.GetFootName(index);
        return string.IsNullOrEmpty(name) ? $"foot_{index}" : name;
    }

    private string GetIKChainNameByBoneIndices(int fixedBoneIndex, int middleBoneIndex, int endBoneIndex)
        => modelInfo.GetIKChainNameByBoneIndices(fixedBoneIndex, middleBoneIndex, endBoneIndex);

    private string FindMatchingAttachmentName(KVObject compiledAttachment) => modelInfo.FindMatchingAttachmentName(compiledAttachment);

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

    private KVObject? ResolveParameterHandle(KVObject handle)
    {
        if (handle == null)
            return null;

        var type = handle.GetStringProperty("m_type");
        var index = handle.GetInt32Property("m_index", -1);

        if (string.IsNullOrEmpty(type) || index < 0 || index == 255)
            return null;

        var typeName = type.Replace("ANIMPARAM_", "", StringComparison.Ordinal);
        if (!typeToParameters.TryGetValue(typeName, out var paramList))
            return null;

        if (index < paramList.Count)
            return paramList[index];

        return null;
    }

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

    private static string GetParameterDescriptionFromObject(KVObject paramObj)
    {
        var className = paramObj.GetStringProperty("_class");
        var typeName = ClassNameToParamType(className);
        var name = paramObj.GetStringProperty("m_name") ?? "Unnamed";

        var result = $"{name} ({typeName})";

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

        var typeRaw = handle.GetStringProperty("m_type", "Unknown");
        var indexRaw = handle.GetInt32Property("m_index", -1);
        return $"{typeRaw} idx {indexRaw}";
    }

    private void DisplayUniversalParameters(KVObject compiledNode, Node node)
    {
        string[] paramFields =
        {
            "m_paramIndex",
            "m_paramX",
            "m_paramY",
            "m_param",
            "m_hParameter",
            "m_hBlendParameter",
            "m_hParam",
        };

        foreach (string field in paramFields)
        {
            if (!compiledNode.ContainsKey(field))
                continue;

            var value = compiledNode[field];

            if (value.ValueType == KVValueType.Int32 || value.ValueType == KVValueType.UInt32)
            {
                int index = value.ToInt32();
                string display = GetParameterDescriptionFromIndex(index);
                node.AddText($"{field}: {display}");

                if (parameterIndexToObject.TryGetValue(index, out var paramFromIndex))
                {
                    RecordParameterConsumer(paramFromIndex, node);
                }

                continue;
            }

            if (value.ValueType == KVValueType.Collection)
            {
                var handle = compiledNode.GetSubCollection(field);
                if (handle != null)
                {
                    string info = GetParameterDescriptionFromHandle(handle);
                    node.AddText($"{field}: {info}");
                    RecordParameterConsumer(ResolveParameterHandle(handle), node);
                }
                continue;
            }

            if (value.ValueType == KVValueType.String)
            {
                string name = value.ToString();
                if (!string.IsNullOrEmpty(name))
                    node.AddText($"{field}: {name}");
            }
        }
    }

    private static (float x, float y) ParseVector2(KVObject obj, string key)
    {
        if (!obj.ContainsKey(key))
            return (0f, 0f);

        var value = obj[key];

        if (value.ValueType == KVValueType.Array)
        {
            var array = obj.GetFloatArray(key);
            if (array != null && array.Length >= 2)
                return (array[0], array[1]);
        }

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

    /// <summary>
    /// Extracts a child node index from a child reference, which may be either an inline
    /// collection holding <c>m_nodeIndex</c> or a bare integer. Returns -1 when absent.
    /// </summary>
    private static int GetChildNodeIndex(KVObject child)
    {
        if (child.ValueType == KVValueType.Collection && child.ContainsKey("m_nodeIndex"))
            return child.GetInt32Property("m_nodeIndex");
        if (child.ValueType == KVValueType.Int32)
            return child.ToInt32();
        return -1;
    }

    private void CreateGraph()
    {
        if (compiledNodes == null || compiledNodes.Count == 0)
            return;

        for (int i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];
            var node = CreateNode(compiledNode, i);
            nodeMap[i] = node;
        }

        for (int i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];
            var parentNode = nodeMap[i];
            var className = compiledNode.GetStringProperty("_class");

            var connections = new List<(int childIdx, string label)>();

            void AddConnection(int idx, string label)
            {
                if (idx >= 0)
                    connections.Add((idx, label));
            }

            if (compiledNode.ContainsKey("m_children"))
            {
                var children = compiledNode.GetArray("m_children");
                foreach (var child in children)
                {
                    int idx = GetChildNodeIndex(child);
                    AddConnection(idx, $"Child {idx}");
                }
            }

            foreach (var prop in ChildProperties)
            {
                if (compiledNode.ContainsKey(prop))
                {
                    var childRef = compiledNode[prop];
                    if (childRef.ValueType == KVValueType.Collection && childRef.ContainsKey("m_nodeIndex"))
                    {
                        int idx = childRef.GetInt32Property("m_nodeIndex");
                        AddConnection(idx, $"Child {idx}");
                    }
                }
            }

            if (className == "CBlend2DUpdateNode" && compiledNode.ContainsKey("m_items"))
            {
                var items = compiledNode.GetArray("m_items");
                for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                {
                    var item = items[itemIdx];
                    if (item.ContainsKey("m_pChild"))
                    {
                        var childRef = item.GetSubCollection("m_pChild");
                        if (childRef.ContainsKey("m_nodeIndex"))
                        {
                            int idx = childRef.GetInt32Property("m_nodeIndex");
                            if (idx >= 0)
                            {
                                int seqIdx = item.GetInt32Property("m_hSequence", -1);
                                string seqDisplay = seqIdx >= 0 ? GetSequenceName(seqIdx) : "None";
                                var pos = ParseVector2(item, "m_vPos");
                                string label = $"Item {itemIdx} (Seq: {seqDisplay}) ({pos.x:F1}, {pos.y:F1})";
                                AddConnection(idx, label);
                            }
                        }
                    }
                }
            }

            if (className == "CStateMachineUpdateNode" && compiledNode.ContainsKey("m_stateData"))
            {
                var stateDataArray = compiledNode.GetArray("m_stateData");
                var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
                if (stateMachine != null && stateMachine.ContainsKey("m_states"))
                {
                    var states = stateMachine.GetArray("m_states");

                    int StateChildNodeIndex(int stateIndex)
                    {
                        if (stateIndex < 0 || stateIndex >= stateDataArray.Count)
                        {
                            return -1;
                        }

                        var stateData = stateDataArray[stateIndex];

                        if (!stateData.ContainsKey("m_pChild"))
                        {
                            return -1;
                        }

                        var childRef = stateData.GetSubCollection("m_pChild");
                        return childRef.ContainsKey("m_nodeIndex") ? childRef.GetInt32Property("m_nodeIndex") : -1;
                    }

                    for (int s = 0; s < stateDataArray.Count; s++)
                    {
                        int idx = StateChildNodeIndex(s);
                        if (idx >= 0)
                        {
                            string stateName = states[s].GetStringProperty("m_name", $"State {s}");
                            AddConnection(idx, stateName);
                        }
                    }

                    // Dashed state-to-state transition wires between the states' subtrees.
                    if (stateMachine.ContainsKey("m_transitions"))
                    {
                        foreach (var transition in stateMachine.GetArray("m_transitions"))
                        {
                            var srcIdx = StateChildNodeIndex(transition.GetInt32Property("m_srcStateIndex"));
                            var destIdx = StateChildNodeIndex(transition.GetInt32Property("m_destStateIndex"));

                            if (srcIdx < 0 || destIdx < 0 || srcIdx == destIdx ||
                                !nodeMap.TryGetValue(srcIdx, out var srcNode) || !nodeMap.TryGetValue(destIdx, out var destNode))
                            {
                                continue;
                            }

                            var from = srcNode.Outputs.Find(static o => o.Name == "Transitions") ?? srcNode.AddOutput("Transitions", GraphHue.Slate);
                            var to = destNode.Inputs.Find(static i => i.Name == "From") ?? destNode.AddInput("From", GraphHue.Slate, allowMultiple: true);

                            if (!to.Wires.Exists(w => w.From == from))
                            {
                                View.Connect(from, to, dashed: true);
                            }
                        }
                    }
                }
            }

            if (className == "CChoiceUpdateNode" && compiledNode.ContainsKey("m_children"))
            {
                var children = compiledNode.GetArray("m_children");
                float[]? weights = compiledNode.ContainsKey("m_weights") ? compiledNode.GetFloatArray("m_weights") : null;
                float[]? blendTimes = compiledNode.ContainsKey("m_blendTimes") ? compiledNode.GetFloatArray("m_blendTimes") : null;

                var newConnections = new List<(int, string)>();
                for (int c = 0; c < children.Count; c++)
                {
                    int idx = GetChildNodeIndex(children[c]);
                    if (idx >= 0)
                    {
                        float w = (weights != null && c < weights.Length) ? weights[c] : 1.0f;
                        float bt = (blendTimes != null && c < blendTimes.Length) ? blendTimes[c] : 0.0f;
                        newConnections.Add((idx, $"Item {c} (W:{w:F2} BT:{bt:F2})"));
                    }
                }
                connections = newConnections;
            }

            if (className == "CSelectorUpdateNode")
            {
                KVObject? paramHandle = null;
                if (compiledNode.ContainsKey("m_hParameter"))
                    paramHandle = compiledNode.GetSubCollection("m_hParameter");
                else if (compiledNode.ContainsKey("m_param"))
                    paramHandle = compiledNode.GetSubCollection("m_param");

                if (paramHandle != null)
                {
                    var paramObj = ResolveParameterHandle(paramHandle);
                    if (paramObj != null && paramObj.GetStringProperty("_class") == "CEnumAnimParameter" && paramObj.ContainsKey("m_enumOptions"))
                    {
                        var enumOptions = paramObj.GetArray<string>("m_enumOptions");
                        if (enumOptions.Length > 0 && compiledNode.ContainsKey("m_children"))
                        {
                            var children = compiledNode.GetArray("m_children");
                            var newConnections = new List<(int, string)>();
                            for (int c = 0; c < children.Count; c++)
                            {
                                int idx = GetChildNodeIndex(children[c]);
                                if (idx >= 0)
                                {
                                    string label = (c < enumOptions.Length) ? enumOptions[c] : $"Option {c}";
                                    newConnections.Add((idx, label));
                                }
                            }
                            connections = newConnections;
                        }
                    }
                }
            }

            foreach (var (childIdx, label) in connections)
            {
                if (!nodeMap.TryGetValue(childIdx, out var childNode))
                    continue;

                if (childNode.Outputs.Count == 0)
                {
                    childNode.AddOutput(string.Empty, PoseHue);
                }

                var inputSocket = parentNode.AddInput(label, PoseHue, allowMultiple: true);
                View.Connect(childNode.Outputs[0], inputSocket);
            }
        }
    }

    /// <summary>
    /// Formats an index-to-name lookup as "Label: name (idx N)", or "Label: None" for negative indices.
    /// </summary>
    private static string FormatIndexed(string label, int index, Func<int, string> resolver)
        => index >= 0 ? $"{label}: {resolver(index)} (idx {index})" : $"{label}: None";

    /// <summary>
    /// Adds a "Label: name (idx N)" text line for an integer property naming a resolvable resource.
    /// </summary>
    private static void AddIndexedName(Node node, KVObject obj, string key, string label, Func<int, string> resolver)
    {
        if (obj.ContainsKey(key))
            node.AddText(FormatIndexed(label, obj.GetInt32Property(key), resolver));
    }

    private static void ApplyNetworkMode(Node node, KVObject obj)
    {
        if (obj.GetStringProperty("m_networkMode", "").Equals("ClientSimulate", StringComparison.Ordinal))
            node.BodyTint = GraphHue.Purple;
    }

    private static string FormatDamping(KVObject damping)
    {
        var speedFunc = damping.GetStringProperty("m_speedFunction", "Unknown");
        var speedScale = damping.GetFloatProperty("m_fSpeedScale", 1.0f);
        return $"{speedFunc} (scale {speedScale:F2})";
    }

    private Node CreateNode(KVObject compiledNode, int index)
    {
        var className = compiledNode.GetStringProperty("_class");
        string displayName = ClassDisplayName.TryGetValue(className, out var display)
            ? display
            : className?.Replace("UpdateNode", "") ?? "Unknown";

        string nodeName = compiledNode.GetStringProperty("m_name");
        if (string.IsNullOrEmpty(nodeName))
            nodeName = displayName;

        if (className is "CSequenceUpdateNode" or "CSingleFrameUpdateNode" or "CCycleControlClipUpdateNode")
        {
            if (compiledNode.ContainsKey("m_hSequence"))
            {
                int seqIdx = compiledNode.GetInt32Property("m_hSequence");
                if (seqIdx >= 0)
                {
                    var seqName = GetSequenceName(seqIdx);
                    if (!string.IsNullOrEmpty(seqName) && !seqName.StartsWith("sequence_"))
                    {
                        nodeName = seqName;
                    }
                }
            }
        }

        var node = new Node(compiledNode)
        {
            Name = $"({index}) {nodeName}",
            NodeType = displayName,
        };

        node.Category = className != null ? HueOfClass(className) : PoseHue;

        ApplyNetworkMode(node, compiledNode);

        HashSet<string>? skipKeys = null;
        if (className != null)
            ClassPropertySkips.TryGetValue(className, out skipKeys);

        foreach (var kv in compiledNode.Children)
        {
            string key = kv.Key;
            if (key == "_class" || key == "m_nodePath" || key == "m_children" || key.StartsWith("m_pChild") ||
                key == "m_tags" || key == "m_paramSpans" || key == "m_stateMachine" || key == "m_stateData" || key == "m_transitionData")
                continue;

            if (GlobalPropertySkips.Contains(key))
                continue;

            if (skipKeys != null && skipKeys.Contains(key))
                continue;

            if (kv.Value.ValueType == KVValueType.Collection || kv.Value.ValueType == KVValueType.Array)
                continue;

            if (key == "m_hSequence")
            {
                node.AddText(FormatIndexed("Sequence", kv.Value.ToInt32(), GetSequenceName));
                continue;
            }

            string displayKey = PropertyDisplayNames.TryGetValue(key, out var friendly) ? friendly : key;
            string valueStr = kv.Value.ToString();
            if (!string.IsNullOrEmpty(valueStr))
                node.AddText($"{displayKey}: {valueStr}");

        }

        DisplayUniversalParameters(compiledNode, node);

        if (compiledNode.ContainsKey("m_nTagIndex"))
        {
            int tagIndex = compiledNode.GetInt32Property("m_nTagIndex");
            if (tagIndex >= 0)
            {
                var tagName = GetTagName(tagIndex);
                node.AddText($"Tag: {tagName} (idx {tagIndex})");
            }
        }

        if (compiledNode.ContainsKey("m_eTagBehavior"))
        {
            node.AddText($"Tag Behavior: {compiledNode.GetStringProperty("m_eTagBehavior")}");
        }

        if (compiledNode.ContainsKey("m_tags"))
        {
            var tagsValue = compiledNode["m_tags"];
            if (tagsValue.ValueType == KVValueType.Array)
            {
                var tagIndices = compiledNode.GetIntegerArray("m_tags");
                if (tagIndices.Length > 0)
                {
                    var tagNames = tagIndices.Select(idx => GetTagName((int)idx));
                    node.AddText($"Tags: [{string.Join(", ", tagNames)}]");
                }
            }
        }

        if (className == "CChoiceUpdateNode")
        {
            if (compiledNode.ContainsKey("m_choiceMethod"))
                node.AddText($"ChoiceMethod: {compiledNode.GetStringProperty("m_choiceMethod")}");
            if (compiledNode.ContainsKey("m_blendMethod"))
                node.AddText($"BlendMethod: {compiledNode.GetStringProperty("m_blendMethod")}");
            if (compiledNode.ContainsKey("m_choiceChangeMethod"))
                node.AddText($"Choice ChangeMethod: {compiledNode.GetStringProperty("m_choiceChangeMethod")}");
            if (compiledNode.ContainsKey("m_bCrossFade"))
                node.AddText($"CrossFade: {compiledNode.GetBooleanProperty("m_bCrossFade")}");
            if (compiledNode.ContainsKey("m_bResetChosen"))
                node.AddText($"ResetChosen: {compiledNode.GetBooleanProperty("m_bResetChosen")}");
            if (compiledNode.ContainsKey("m_bDontResetSameSelection"))
                node.AddText($"DontResetSameSelection: {compiledNode.GetBooleanProperty("m_bDontResetSameSelection")}");
        }

        if (className == "CSequenceUpdateNode")
        {
            if (compiledNode.ContainsKey("m_duration"))
                node.AddText($"Duration: {compiledNode.GetFloatProperty("m_duration"):F2}");
            if (compiledNode.ContainsKey("m_playbackSpeed"))
                node.AddText($"Speed: {compiledNode.GetFloatProperty("m_playbackSpeed"):F2}");
            if (compiledNode.ContainsKey("m_bLoop"))
                node.AddText($"Loop: {compiledNode.GetBooleanProperty("m_bLoop")}");
        }

        if (className == "CBlend2DUpdateNode")
        {
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

            if (compiledNode.ContainsKey("m_damping"))
            {
                var damping = compiledNode.GetSubCollection("m_damping");
                if (damping != null)
                {
                    node.AddText($"Damping: {FormatDamping(damping)}");
                }
            }

            if (compiledNode.ContainsKey("m_items"))
            {
                var items = compiledNode.GetArray("m_items");
                node.AddText($"Items: {items.Count}");
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    int seqIdx = item.GetInt32Property("m_hSequence", -1);
                    string seqDisplay = seqIdx >= 0 ? GetSequenceName(seqIdx) : "None";
                    var pos = ParseVector2(item, "m_vPos");
                    float dur = item.GetFloatProperty("m_flDuration");
                    node.AddText($"  [{i}] Seq: {seqDisplay} ({pos.x:F1}, {pos.y:F1}) dur={dur:F2}s");
                }
            }
        }

        if (className == "CBoneMaskUpdateNode")
        {
            AddIndexedName(node, compiledNode, "m_nWeightListIndex", "Bone Mask", GetWeightListName);
            if (compiledNode.ContainsKey("m_blendSpace"))
                node.AddText($"Blend Space: {compiledNode.GetStringProperty("m_blendSpace")}");
            if (compiledNode.ContainsKey("m_flRootMotionBlend"))
                node.AddText($"Root Motion Blend: {compiledNode.GetFloatProperty("m_flRootMotionBlend"):F2}");
            if (compiledNode.ContainsKey("m_bUseBlendScale"))
                node.AddText($"Use Blend Scale: {compiledNode.GetBooleanProperty("m_bUseBlendScale")}");
        }

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
        if (className == "CSelectorUpdateNode")
        {
            if (compiledNode.ContainsKey("m_selectionSource"))
                node.AddText($"SelectionSource: {compiledNode.GetStringProperty("m_selectionSource")}");
        }

        if (className == "CTwoBoneIKUpdateNode" && compiledNode.ContainsKey("m_opFixedData"))
        {
            var opFixedData = compiledNode.GetSubCollection("m_opFixedData");

            if (opFixedData.ContainsKey("m_endEffectorType"))
                node.AddText($"End Effector Type: {opFixedData.GetStringProperty("m_endEffectorType")}");

            if (opFixedData.ContainsKey("m_targetType"))
                node.AddText($"Target Type: {opFixedData.GetStringProperty("m_targetType")}");

            int fixedIdx = opFixedData.GetInt32Property("m_nFixedBoneIndex", -1);
            int middleIdx = opFixedData.GetInt32Property("m_nMiddleBoneIndex", -1);
            int endIdx = opFixedData.GetInt32Property("m_nEndBoneIndex", -1);
            if (fixedIdx >= 0 && middleIdx >= 0 && endIdx >= 0)
            {
                var chainName = GetIKChainNameByBoneIndices(fixedIdx, middleIdx, endIdx);
                if (!string.IsNullOrEmpty(chainName))
                    node.AddText($"IK Chain: {chainName}");
            }

            // Attachments
            //if (opFixedData.ContainsKey("m_endEffectorAttachment"))
            //{
            //var attachObj = opFixedData.GetSubCollection("m_endEffectorAttachment");
            //var name = FindMatchingAttachmentName(attachObj);
            //node.AddText($"End Effector Attachment: {(!string.IsNullOrEmpty(name) ? name : "(unresolved)")}");
            //}
            //if (opFixedData.ContainsKey("m_targetAttachment"))
            //{
            //var attachObj = opFixedData.GetSubCollection("m_targetAttachment");
            //var name = FindMatchingAttachmentName(attachObj);
            //node.AddText($"Target Attachment: {(!string.IsNullOrEmpty(name) ? name : "(unresolved)")}");
            //}

            if (opFixedData.ContainsKey("m_hPositionParam"))
            {
                var handle = opFixedData.GetSubCollection("m_hPositionParam");
                node.AddText($"Position Param: {GetParameterDescriptionFromHandle(handle)}");
            }
            if (opFixedData.ContainsKey("m_hRotationParam"))
            {
                var handle = opFixedData.GetSubCollection("m_hRotationParam");
                node.AddText($"Rotation Param: {GetParameterDescriptionFromHandle(handle)}");
            }

            string[] extraProps = { "m_bAlwaysUseFallbackHinge", "m_vLsFallbackHingeAxis", "m_bMatchTargetOrientation", "m_bConstrainTwist", "m_flMaxTwist" };
            foreach (var prop in extraProps)
            {
                if (opFixedData.ContainsKey(prop))
                {
                    var val = opFixedData[prop];
                    if (!val.IsCollection && !val.IsArray)
                        node.AddText($"{prop}: {val}");
                }
            }
        }
        if (className == "CAimMatrixUpdateNode")
        {
            if (compiledNode.ContainsKey("m_target"))
                node.AddText($"Target: {compiledNode.GetStringProperty("m_target")}");

            AddIndexedName(node, compiledNode, "m_hSequence", "Sequence", GetSequenceName);

            if (compiledNode.ContainsKey("m_bResetChild"))
                node.AddText($"Reset Child: {compiledNode.GetBooleanProperty("m_bResetChild")}");

            if (compiledNode.ContainsKey("m_bLockWhenWaning"))
                node.AddText($"Lock When Waning: {compiledNode.GetBooleanProperty("m_bLockWhenWaning")}");

            if (compiledNode.ContainsKey("m_opFixedSettings"))
            {
                var settings = compiledNode.GetSubCollection("m_opFixedSettings");

                if (settings.ContainsKey("m_attachment"))
                {
                    var attachObj = settings.GetSubCollection("m_attachment");
                    var name = FindMatchingAttachmentName(attachObj);
                    node.AddText($"Attachment: {(!string.IsNullOrEmpty(name) ? name : "(unresolved)")}");
                }

                if (settings.ContainsKey("m_damping"))
                {
                    node.AddText($"Damping: {FormatDamping(settings.GetSubCollection("m_damping"))}");
                }

                if (settings.ContainsKey("m_eBlendMode"))
                    node.AddText($"Blend Mode: {settings.GetStringProperty("m_eBlendMode")}");

                if (settings.ContainsKey("m_flMaxYawAngle"))
                    node.AddText($"Max Yaw Angle: {settings.GetFloatProperty("m_flMaxYawAngle"):F2}");

                if (settings.ContainsKey("m_flMaxPitchAngle"))
                    node.AddText($"Max Pitch Angle: {settings.GetFloatProperty("m_flMaxPitchAngle"):F2}");

                AddIndexedName(node, settings, "m_nBoneMaskIndex", "Bone Mask", GetWeightListName);

                if (settings.ContainsKey("m_bTargetIsPosition"))
                    node.AddText($"Target Is Position: {settings.GetBooleanProperty("m_bTargetIsPosition")}");

                if (settings.ContainsKey("m_bUseBiasAndClamp"))
                    node.AddText($"Use Bias And Clamp: {settings.GetBooleanProperty("m_bUseBiasAndClamp")}");

                if (settings.ContainsKey("m_flBiasAndClampYawOffset"))
                    node.AddText($"Bias/Clamp Yaw Offset: {settings.GetFloatProperty("m_flBiasAndClampYawOffset"):F2}");

                if (settings.ContainsKey("m_flBiasAndClampPitchOffset"))
                    node.AddText($"Bias/Clamp Pitch Offset: {settings.GetFloatProperty("m_flBiasAndClampPitchOffset"):F2}");

                if (settings.ContainsKey("m_biasAndClampBlendCurve"))
                {
                    var curve = settings.GetSubCollection("m_biasAndClampBlendCurve");
                    var cp1 = curve.GetFloatProperty("m_flControlPoint1", 0f);
                    var cp2 = curve.GetFloatProperty("m_flControlPoint2", 1f);
                    node.AddText($"Bias/Clamp Curve: ({cp1:F2}, {cp2:F2})");
                }
            }
        }
        if (className == "CFollowAttachmentUpdateNode" && compiledNode.ContainsKey("m_opFixedData"))
        {
            var opFixedData = compiledNode.GetSubCollection("m_opFixedData");

            AddIndexedName(node, opFixedData, "m_boneIndex", "Bone", GetBoneName);

            if (opFixedData.ContainsKey("m_attachment"))
            {
                var attachObj = opFixedData.GetSubCollection("m_attachment");
                var name = FindMatchingAttachmentName(attachObj);
                node.AddText($"Attachment: {(!string.IsNullOrEmpty(name) ? name : "(unresolved)")}");
            }

            if (opFixedData.ContainsKey("m_bMatchTranslation"))
                node.AddText($"Match Translation: {opFixedData.GetBooleanProperty("m_bMatchTranslation")}");
            if (opFixedData.ContainsKey("m_bMatchRotation"))
                node.AddText($"Match Rotation: {opFixedData.GetBooleanProperty("m_bMatchRotation")}");
        }

        if (className == "CFootPinningUpdateNode")
        {
            if (compiledNode.ContainsKey("m_eTimingSource"))
                node.AddText($"Timing Source: {compiledNode.GetStringProperty("m_eTimingSource")}");
            if (compiledNode.ContainsKey("m_bResetChild"))
                node.AddText($"Reset Child: {compiledNode.GetBooleanProperty("m_bResetChild")}");

            if (compiledNode.ContainsKey("m_params"))
            {
                var paramsArray = compiledNode.GetArray("m_params");
                if (paramsArray != null && paramsArray.Count > 0)
                {
                    var paramDescs = paramsArray.Select(h => GetParameterDescriptionFromHandle(h));
                    node.AddText($"Params: {string.Join(", ", paramDescs)}");
                }
            }

            if (compiledNode.ContainsKey("m_poseOpFixedData"))
            {
                var poseData = compiledNode.GetSubCollection("m_poseOpFixedData");

                if (poseData.ContainsKey("m_flBlendTime"))
                    node.AddText($"Blend Time: {poseData.GetFloatProperty("m_flBlendTime"):F2}");
                if (poseData.ContainsKey("m_flLockBreakDistance"))
                    node.AddText($"Lock Break Distance: {poseData.GetFloatProperty("m_flLockBreakDistance"):F2}");
                if (poseData.ContainsKey("m_flMaxLegTwist"))
                    node.AddText($"Max Leg Twist: {poseData.GetFloatProperty("m_flMaxLegTwist"):F2}");

                AddIndexedName(node, poseData, "m_nHipBoneIndex", "Hip Bone", GetBoneName);

                if (poseData.ContainsKey("m_bApplyLegTwistLimits"))
                    node.AddText($"Apply Leg Twist Limits: {poseData.GetBooleanProperty("m_bApplyLegTwistLimits")}");
                if (poseData.ContainsKey("m_bApplyFootRotationLimits"))
                    node.AddText($"Apply Foot Rotation Limits: {poseData.GetBooleanProperty("m_bApplyFootRotationLimits")}");

                if (poseData.ContainsKey("m_footInfo"))
                {
                    var footInfoArray = poseData.GetArray("m_footInfo");
                    if (footInfoArray.Count > 0)
                    {
                        node.AddText($"Feet ({footInfoArray.Count}):");
                        for (int i = 0; i < footInfoArray.Count; i++)
                        {
                            var foot = footInfoArray[i];
                            var footLines = new List<string>();

                            if (foot.ContainsKey("m_nFootIndex"))
                                footLines.Add(FormatIndexed("Foot", foot.GetInt32Property("m_nFootIndex"), GetFootName));
                            if (foot.ContainsKey("m_nTargetBoneIndex"))
                                footLines.Add(FormatIndexed("Target Bone", foot.GetInt32Property("m_nTargetBoneIndex"), GetBoneName));
                            if (foot.ContainsKey("m_nAnkleBoneIndex"))
                                footLines.Add(FormatIndexed("Ankle Bone", foot.GetInt32Property("m_nAnkleBoneIndex"), GetBoneName));
                            if (foot.ContainsKey("m_nIKAnchorBoneIndex"))
                                footLines.Add(FormatIndexed("IK Anchor Bone", foot.GetInt32Property("m_nIKAnchorBoneIndex"), GetBoneName));
                            if (foot.ContainsKey("m_ikChainIndex"))
                                footLines.Add(FormatIndexed("IK Chain", foot.GetInt32Property("m_ikChainIndex"), GetIKChainName));
                            if (foot.ContainsKey("m_nTagIndex"))
                                footLines.Add(FormatIndexed("Tag", foot.GetInt32Property("m_nTagIndex"), GetTagName));
                            if (foot.ContainsKey("m_flMaxIKLength"))
                                footLines.Add($"Max IK Length: {foot.GetFloatProperty("m_flMaxIKLength"):F2}");
                            if (foot.ContainsKey("m_flMaxRotationLeft"))
                                footLines.Add($"Max Rotation Left: {foot.GetFloatProperty("m_flMaxRotationLeft"):F2}");
                            if (foot.ContainsKey("m_flMaxRotationRight"))
                                footLines.Add($"Max Rotation Right: {foot.GetFloatProperty("m_flMaxRotationRight"):F2}");

                            node.AddText($"  [{i}] {string.Join(", ", footLines)}");
                        }
                    }
                }
            }
        }

        View.AddNode(node);
        return node;
    }

    private static GraphHue GetParameterTypeHue(string type)
    {
        return type switch
        {
            "BOOL" => GraphHue.Blue,
            "INT" => GraphHue.Orange,
            "FLOAT" => GraphHue.Olive,
            "ENUM" => GraphHue.Purple,
            "VECTOR" => GraphHue.Green,
            "QUATERNION" => GraphHue.Maroon,
            "SYMBOL" => GraphHue.Neutral,
            "VIRTUAL" => GraphHue.Neutral,
            _ => GraphHue.Neutral,
        };
    }

    private void RecordParameterConsumer(KVObject? paramObj, Node node)
    {
        if (paramObj == null)
        {
            return;
        }

        if (!parameterConsumers.TryGetValue(paramObj, out var list))
        {
            list = [];
            parameterConsumers[paramObj] = list;
        }

        if (!list.Contains(node))
        {
            list.Add(node);
        }
    }

    private void AddParameterAndTagNodes()
    {
        var typeOrder = new[] { "BOOL", "INT", "FLOAT", "ENUM", "VECTOR", "QUATERNION", "SYMBOL", "VIRTUAL", "UNKNOWN" };
        foreach (var type in typeOrder)
        {
            if (!typeToParameters.TryGetValue(type, out var list)) continue;
            if (list.Count == 0) continue;

            var ordered = list.OrderBy(p => parameterObjectToIndex.TryGetValue(p, out var idx) ? idx : int.MaxValue).ToList();
            var friendlyName = ParameterTypeDisplayName.TryGetValue(type, out var display) ? display : type;

            var node = new Node(null)
            {
                Name = $"{friendlyName} Parameters",
                NodeType = "Parameter Group",
                Category = GetParameterTypeHue(type),
            };
            foreach (var p in ordered)
            {
                if (drawParameterWires && parameterConsumers.TryGetValue(p, out var consumers))
                {
                    // Consumed parameters become output sockets wired to their readers, making
                    // "what does this parameter drive" visible.
                    var output = node.AddOutput(GetParameterDescriptionFromObject(p), GetParameterTypeHue(type));

                    foreach (var consumer in consumers)
                    {
                        var input = consumer.Inputs.Find(static i => i.Name == "Params") ?? consumer.AddInput("Params", GraphHue.Neutral, allowMultiple: true);
                        View.Connect(output, input, dashed: true);
                    }
                }
                else
                {
                    node.AddText(GetParameterDescriptionFromObject(p));
                }
            }

            View.AddNode(node);
        }

        if (tags.Count > 0)
        {
            var tagIndexMap = tags.Select((t, i) => new { t, i }).ToDictionary(x => x.t, x => x.i);

            var tagGroups = tags.GroupBy(t => t.GetStringProperty("_class") ?? "Unknown");
            foreach (var group in tagGroups)
            {
                var className = group.Key;
                var friendlyName = TagDisplayName.TryGetValue(className, out var display) ? display : className;

                var ordered = group.OrderBy(t => tagIndexMap.TryGetValue(t, out var idx) ? idx : int.MaxValue).ToList();

                var node = new Node(null)
                {
                    Name = $"{friendlyName}",
                    NodeType = "Tag Group",
                    Category = GetTagClassHue(className),
                };
                foreach (var t in ordered)
                {
                    var name = t.GetStringProperty("m_name") ?? "Unnamed";
                    node.AddText(name);
                }
                View.AddNode(node);
            }
        }
    }

    private void AddComponentNodes()
    {
        if (components.Count == 0)
            return;

        foreach (var comp in components)
        {
            var className = comp.GetStringProperty("_class") ?? "Unknown";
            var friendlyName = ComponentDisplayName.TryGetValue(className, out var display) ? display : className;
            var name = comp.GetStringProperty("m_name") ?? "";
            var displayName = string.IsNullOrEmpty(name) ? friendlyName : $"{friendlyName} ({name})";

            var node = new Node(null)
            {
                Name = displayName,
                NodeType = "Component",
                Category = GetComponentClassHue(className),
            };

            if (className == "CStateMachineComponentUpdater")
            {
                if (comp.ContainsKey("m_stateMachine"))
                {
                    var stateMachine = comp.GetSubCollection("m_stateMachine");
                    if (stateMachine.ContainsKey("m_states"))
                    {
                        var states = stateMachine.GetArray("m_states");
                        node.AddText($"States ({states.Count}):");
                        foreach (var state in states)
                        {
                            var stateName = state.GetStringProperty("m_name", "Unnamed");
                            node.AddText($"  {stateName}");
                        }
                    }
                }
            }

            foreach (var kv in comp.Children)
            {
                string key = kv.Key;
                if (key == "_class" || key == "m_stateMachine" || key == "m_name")
                    continue;
                if (kv.Value.ValueType == KVValueType.Collection || kv.Value.ValueType == KVValueType.Array)
                    continue;

                string displayKey = PropertyDisplayNames.TryGetValue(key, out var friendly) ? friendly : key;
                string valueStr = kv.Value.ToString();
                if (!string.IsNullOrEmpty(valueStr))
                    node.AddText($"{displayKey}: {valueStr}");
            }

            ApplyNetworkMode(node, comp);

            View.AddNode(node);
        }
    }

    public override void Dispose()
    {
        modelResource?.Dispose();
        modelResource = null;
        modelResourceLoaded = false;

        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
