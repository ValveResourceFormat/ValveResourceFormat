using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

public sealed partial class VMixGraphExtract
{
    private readonly record struct PlugSource(string Node, string Plug);

    private sealed class MixNode(string name, string type)
    {
        public string Name { get; set; } = name;
        public string Type { get; set; } = type;
        public string? FriendlyName { get; set; }
        public KVObject Properties { get; set; } = KVObject.Collection();
        public List<KeyValuePair<string, PlugSource>> Inputs { get; } = [];
        public MixProcessor? Processor { get; set; }

        /// <summary>Control plugs of a processor and the value each one reads.</summary>
        public Dictionary<string, int> ControlValues { get; } = new(StringComparer.Ordinal);

        public bool IsAccumulatingMix { get; set; }

        /// <summary>Whether a subgraph switch names its plugs after every subgraph instead of the first.</summary>
        public bool DetailedPlugs { get; set; }
        public int MeteredValue { get; set; } = -1;

        public void SetInput(string plug, PlugSource source)
        {
            var index = Inputs.FindIndex(input => input.Key == plug);

            if (index >= 0)
            {
                Inputs[index] = new(plug, source);
            }
            else
            {
                Inputs.Add(new(plug, source));
            }
        }

        public bool HasInput(string plug) => Inputs.Exists(input => input.Key == plug);
    }

    /// <summary>
    /// Rebuilds the editor nodes and edges of one compiled graph. Each node compiles to a recognisable run of
    /// commands, named by the debug hashes ("node/plug") and meters the compiler emits along with it.
    /// </summary>
    private sealed class MixGraphDecompiler
    {
        private const int MaxNodeIndex = 400;

        private readonly MixGraph graph;
        private readonly List<MixCommand> commands;
        private readonly Dictionary<uint, string> names = [];
        private readonly OrderedDictionary<string, MixNode> nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<int>> reserved = new(StringComparer.Ordinal);
        private readonly Dictionary<int, PlugSource> submixHolders = [];
        private readonly Dictionary<int, PlugSource> valueSources = [];
        private readonly Dictionary<(string Node, string Plug), int> meteredInputs = [];
        private readonly Dictionary<int, List<(string Node, string Plug)>> meteredOutputs = [];
        private readonly Dictionary<int, MixNode> processorNodes = [];
        private readonly List<(int Command, int Value, string Node)> controlStores = [];
        private readonly List<(int Start, List<string> Created)> blocks = [];
        private readonly Dictionary<int, string> inputNodes = [];

        public List<string> Warnings { get; } = [];

        public MixGraphDecompiler(MixGraph graph)
        {
            this.graph = graph;
            commands = graph.Commands;
            BuildNameTable();
        }

        public IEnumerable<MixNode> Nodes => nodes.Values;

        private static string? TypeOf(string name)
        {
            // Nodes of inlined subgraphs are named after the subgraph node, then their own name
            if (name.LastIndexOf('/') is var slash and >= 0)
            {
                name = name[(slash + 1)..];
            }

            string? best = null;

            foreach (var type in NodeTypes)
            {
                if (name.Length > type.Name.Length && name.StartsWith(type.Name, StringComparison.Ordinal)
                    && name.AsSpan(type.Name.Length).ContainsAnyExceptInRange('0', '9') == false
                    && (best == null || type.Name.Length > best.Length))
                {
                    best = type.Name;
                }
            }

            return best;
        }

        private static IEnumerable<string> PlugsOf(string? type)
            => type != null && NodeTypesByName.TryGetValue(type, out var nodeType) ? nodeType.Plugs : [];

        private static readonly string[] GenericPlugs =
        [
            "audio:Out", "audio:Source", "audio:In", "value", "value:Out", "valueOut", "out", "Effect Name", "Vsnd Name", "effect",
            "audio.Output1", "audio.Track1", "Selection Name", "Selection Index", "dBLevelOut", "LevelOut", "ReductionOut",
        ];

        private void BuildNameTable()
        {
            var targets = new HashSet<uint>();

            foreach (var command in commands)
            {
                if (command.ParamName != 0)
                {
                    targets.Add(command.ParamName);
                }
            }

            foreach (var processor in graph.Processors)
            {
                targets.Add(processor.DebugId);
            }

            targets.UnionWith(graph.AudioMeterDebugIds);
            targets.UnionWith(graph.Submixes.Select(static submix => submix.SoloNameHash));
            targets.Remove(0);

            var allPlugs = NodeTypes.SelectMany(static type => type.Plugs).Concat(GenericPlugs).Distinct(StringComparer.Ordinal).ToArray();

            void Try(string name)
            {
                var hash = StringToken.Get(name);

                if (targets.Contains(hash))
                {
                    names.TryAdd(hash, name);
                }
            }

            var literals = new HashSet<string>(StringComparer.Ordinal);
            literals.UnionWith(graph.AudioMeters);
            literals.UnionWith(graph.ControlMeters.Select(static meter => meter.Name));
            literals.UnionWith(graph.NameInputMeters.Select(static meter => meter.Name));
            literals.UnionWith(graph.Processors.Select(static processor => processor.Name));
            literals.UnionWith(graph.Submixes.Select(static submix => submix.Name));
            literals.UnionWith(graph.ControlInputs.Select(static input => input.Name));
            literals.UnionWith(graph.NameInputs.Select(static input => input.Name));
            literals.UnionWith(graph.VsndInputs.Select(static input => input.Name));

            // Inlined subgraphs prefix the names of their nodes with the name of the subgraph node
            var prefixes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var literal in literals.ToArray())
            {
                var segments = literal.Split('/');

                for (var k = 1; k < segments.Length; k++)
                {
                    literals.Add(string.Join('/', segments[..k]));

                    if (k < segments.Length && TypeOf(segments[k]) != null)
                    {
                        prefixes.Add(string.Join('/', segments[..k]));
                    }
                }
            }

            foreach (var literal in literals)
            {
                Try(literal);

                foreach (var plug in TypeOf(literal) is { } type ? PlugsOf(type).Concat(GenericPlugs) : allPlugs)
                {
                    Try(string.Concat(literal, "/", plug));
                }
            }

            foreach (var target in targets)
            {
                if (NodeNames.Value.TryGetValue(target, out var name))
                {
                    names.TryAdd(target, name);
                }
            }

            // Only the node types the graph names anywhere are tried inside inlined subgraphs
            var innerTypes = literals.SelectMany(static literal => literal.Split('/')).Select(TypeOf)
                .Concat(graph.Processors.Select(static processor => processor.NodeType))
                .Concat(["audio.Track", "audio.Output", "control.Input", "control.Output"])
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(static type => NodeTypesByName[type])
                .ToList();

            foreach (var prefix in prefixes)
            {
                if (targets.All(names.ContainsKey))
                {
                    break;
                }

                if (prefix.Length < 256)
                {
                    BruteForce(prefix + "/", 64, innerTypes, (hash, name) =>
                    {
                        if (targets.Contains(hash))
                        {
                            names.TryAdd(hash, name.ToString());
                        }
                    });
                }
            }
        }

        private delegate void NameCandidate(uint hash, ReadOnlySpan<char> name);

        /// <summary>
        /// The names a node gets in a graph that compiles it without prefix, "audio.Filter3" and "audio.Filter3/audio:Out", by their tokens.
        /// </summary>
        private static readonly Lazy<Dictionary<uint, string>> NodeNames = new(static () =>
        {
            var table = new Dictionary<uint, string>();
            BruteForce(string.Empty, MaxNodeIndex, NodeTypes, (hash, name) => table.TryAdd(hash, name.ToString()));
            return table;
        });

        /// <summary>
        /// Offers every node name and node plug name of the node types, numbered up to <paramref name="maxIndex"/>.
        /// </summary>
        private static void BruteForce(string prefix, int maxIndex, IEnumerable<NodeType> types, NameCandidate candidate)
        {
            Span<char> buffer = stackalloc char[512];
            prefix.CopyTo(buffer);

            foreach (var type in types)
            {
                var plugs = type.Plugs.Concat(GenericPlugs).Distinct(StringComparer.Ordinal).ToArray();
                type.Name.CopyTo(buffer[prefix.Length..]);
                var typeLength = prefix.Length + type.Name.Length;

                for (var index = 1; index <= maxIndex; index++)
                {
                    index.TryFormat(buffer[typeLength..], out var digits, provider: CultureInfo.InvariantCulture);
                    var nodeLength = typeLength + digits;
                    candidate(StringToken.Get(buffer[..nodeLength]), buffer[..nodeLength]);

                    buffer[nodeLength] = '/';

                    foreach (var plug in plugs)
                    {
                        if (nodeLength + 1 + plug.Length > buffer.Length)
                        {
                            continue;
                        }

                        plug.CopyTo(buffer[(nodeLength + 1)..]);
                        var full = buffer[..(nodeLength + 1 + plug.Length)];
                        candidate(StringToken.Get(full), full);
                    }
                }
            }
        }

        private string? NameOf(uint hash) => hash != 0 && names.TryGetValue(hash, out var name) ? name : null;

        private static (string Node, string Plug)? Split(string? name)
        {
            if (name == null || name.LastIndexOf('/') is not (var slash and > 0))
            {
                return null;
            }

            return (name[..slash], name[(slash + 1)..]);
        }

        private MixNode Node(string name, string? type = null)
        {
            if (!nodes.TryGetValue(name, out var node))
            {
                node = new MixNode(name, type ?? TypeOf(name) ?? "audio.Processor");
                nodes.Add(name, node);
            }

            return node;
        }

        private string Fresh(string type)
        {
            var used = reserved.TryGetValue(type, out var set) ? set : reserved[type] = [];

            foreach (var name in nodes.Keys)
            {
                if (name.StartsWith(type, StringComparison.Ordinal) && int.TryParse(name.AsSpan(type.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    used.Add(index);
                }
            }

            var next = 1;

            while (used.Contains(next))
            {
                next++;
            }

            used.Add(next);
            return string.Create(CultureInfo.InvariantCulture, $"{type}{next}");
        }

        private void Reserve(string? name)
        {
            if (name == null || name.Contains('/', StringComparison.Ordinal) || TypeOf(name) is not { } type)
            {
                return;
            }

            if (int.TryParse(name.AsSpan(type.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                (reserved.TryGetValue(type, out var set) ? set : reserved[type] = []).Add(index);
            }
        }

        private void Edge(MixNode node, string plug, PlugSource? source)
        {
            if (source is not { } value)
            {
                Warnings.Add($"no source for {node.Name}/{plug}");
                return;
            }

            node.SetInput(plug, value);
        }

        private MixCommand? Next(int index, MixOp op) => index < commands.Count && commands[index].Op == op ? commands[index] : null;

        private string? MeterName(MixCommand command)
            => command.Value0 >= 0 && command.Value0 < graph.AudioMeters.Count ? graph.AudioMeters[command.Value0] : null;

        private PlugSource? Source(int value) => valueSources.TryGetValue(value, out var source) ? source : null;

        public MixGraphDecompiler Run()
        {
            foreach (var name in names.Values)
            {
                Reserve(Split(name)?.Node ?? name);
            }

            foreach (var meter in graph.AudioMeters)
            {
                Reserve(Split(meter)?.Node ?? meter);
            }

            foreach (var meter in graph.ControlMeters)
            {
                if (Split(meter.Name) is not var (node, plug))
                {
                    continue;
                }

                Reserve(node);
                var type = TypeOf(node);

                if (type != null && (IsOutputPlug(type, plug) || plug is "value" && type is "control.Input" or "control.InputArray" or "control.StackInput"))
                {
                    (meteredOutputs.TryGetValue(meter.Value, out var list) ? list : meteredOutputs[meter.Value] = []).Add((node, plug));
                }
                else
                {
                    meteredInputs[(node, plug)] = meter.Value;
                }
            }

            if (graph.Format == MixGraphFormat.Heap)
            {
                CreateGraphInputNodes();
            }

            var i = 0;

            while (i < commands.Count)
            {
                var before = nodes.Count;
                var next = Handle(i);
                blocks.Add((i, [.. nodes.Keys.Skip(before)]));
                i = Math.Max(next, i + 1);
            }

            Finish();
            return this;
        }

        private static bool IsOutputPlug(string type, string plug)
            => NodeTypesByName.TryGetValue(type, out var nodeType) && nodeType.Outputs.Contains(plug, StringComparer.Ordinal)
                || plug is "value:Out" or "valueOut" or "inverseOut" or "fadeInValue" or "fadeOutValue" or "out" or "dBLevelOut" or "LevelOut" or "ReductionOut";

        private int Handle(int i)
        {
            var command = commands[i];

            switch (command.Op)
            {
                case MixOp.SubmixDebug: return SubmixDebug(i, command);
                case MixOp.SubmixMeter: return SubmixMeter(i, command);
                case MixOp.SubmixMeterSpectrum:
                    if (InnerBoundary(NameOf(command.ParamName), command.InputSubmix0))
                    {
                        return i + 1;
                    }

                    if (Split(NameOf(command.ParamName)) is (var meterNode, "audio:In") && TypeOf(meterNode) == "audio.Meter")
                    {
                        AudioMeter(i, meterNode, command.InputSubmix0);
                        return i + 1;
                    }

                    if (graph.Format == MixGraphFormat.Heap && Next(i + 1, MixOp.SubmixProcess) != null)
                    {
                        return ProcessorBlock(i + 1);
                    }

                    return i + 1;
                case MixOp.ProcessorSetControlValue:
                case MixOp.ProcessorSetControlArrayValue:
                case MixOp.ProcessorSetVsndValue:
                case MixOp.ProcessorSetNameInput:
                case MixOp.ProcessorSetImpulseResponseValue:
                case MixOp.SubmixGenerateSidechain:
                case MixOp.SubmixProcess:
                case MixOp.SubmixGenerate:
                    return ProcessorBlock(i);
                case MixOp.ProcessorStoreControlValue:
                    if (processorNodes.TryGetValue(command.Processor, out var owner))
                    {
                        valueSources[command.Value0] = new(owner.Name, StorePlug(command.ParamName));
                    }

                    return i + 1;
                case MixOp.ControlCondCopyIfNegative:
                    if (Next(i + 1, MixOp.SubmixCopy) is { } copy && copy.Value0 == command.Value0 && copy.Value1 == command.Value1)
                    {
                        // A splitter with only the output that keeps its input ramps like an amp, but meters as a splitter
                        if (Next(i + 3, MixOp.SubmixMeter) is { } rampMeter && Split(MeterName(rampMeter)) is var (rampNode, _) && TypeOf(rampNode) == "audio.Splitter")
                        {
                            return SplitterBlock(i);
                        }

                        return AmpBlock(i + 1);
                    }

                    return i + 1;
                case MixOp.SubmixCopy:
                case MixOp.SubmixAccumulate:
                    return SubmixCopy(i, command);
                case MixOp.SubmixOutput:
                    return OutputBlock(i, [command.InputSubmix0], [command.Value0], command);
                case MixOp.SubmixOutputX2:
                    return OutputBlock(i, [command.InputSubmix0, command.InputSubmix1], [command.Value0, command.Value1], command);
                case MixOp.ControlInputStore:
                case MixOp.ControlInputStoreDecibels:
                    return ControlStore(i, command);
                case MixOp.ControlCopy when Next(i + 1, MixOp.ControlSineBlend) != null:
                    return SplitterBlendBlock(i);
                case MixOp.ControlCopy:
                case MixOp.ControlEvaluateCurve:
                case MixOp.ControlRemapLinear:
                case MixOp.ControlRemapLogLinear:
                case MixOp.ControlRemapSine:
                    return ControlBlock(i);
                case MixOp.ControlMax:
                    return ControlMax(i, command);
                case MixOp.ControlOutputStore:
                    return ControlOutput(i, command);
                case MixOp.ControlTransientInputStore:
                    return TransientInput(i, command);
                case MixOp.ControlResetTimer:
                case MixOp.ControlIncrementTimer:
                case MixOp.ControlTransientInputReset:
                    return i + 1;
                case MixOp.ControlEvalEnvelope:
                    return Envelope(i, command);
                default:
                    Warnings.Add($"unhandled command {i} {command.Name}");
                    return i + 1;
            }
        }

        private static string StorePlug(uint hash)
        {
            if (hash == StringToken.Get("reduction"))
            {
                return "ReductionOut";
            }

            if (hash == StringToken.Get("dblevel"))
            {
                return "dBLevelOut";
            }

            return hash == StringToken.Get("level") ? "LevelOut" : hash.ToString(CultureInfo.InvariantCulture);
        }

        private int SubmixDebug(int i, MixCommand command)
        {
            if (Split(NameOf(command.ParamName)) is not var (nodeName, plug))
            {
                Warnings.Add($"debug {i}: unknown hash {command.ParamName}");
                submixHolders[command.InputSubmix0] = new(string.Create(CultureInfo.InvariantCulture, $"?{command.ParamName}"), "audio:Out");
                return i + 1;
            }

            var node = Node(nodeName);
            submixHolders[command.InputSubmix0] = new(nodeName, plug);

            if (plug != "audio:Source" || command.InputSubmix0 >= graph.Submixes.Count)
            {
                return i + 1;
            }

            node.Type = "audio.Track";
            var submix = graph.Submixes[command.InputSubmix0];
            node.FriendlyName = submix.Name;
            node.Properties = TrackProperties(submix);

            if (Next(i + 1, MixOp.SubmixMeter) is { } meter && meter.InputSubmix0 == command.InputSubmix0)
            {
                return i + 2;
            }

            return i + 1;
        }

        private KVObject TrackProperties(MixSubmix submix)
        {
            var properties = KVObject.Collection();

            if (graph.Format == MixGraphFormat.Heap)
            {
                properties.Add("m_nChannels", submix.Channels);
                properties.Add("m_nMixDown", submix.MixDownRule.Length > 0 ? submix.MixDownRule : "SUM");
                properties.Add("m_nSendOperator", submix.SendOperator);
                properties.Add("m_SendNames", KVObject.Array(submix.Sends.Select(static send => (KVObject)send)));

                if (submix.UpMixRule.Length > 0)
                {
                    properties.Add("m_nUpMixRule", submix.UpMixRule);
                }

                return properties;
            }

            properties.Add("m_trackName", submix.Name);
            properties.Add("m_nChannels", submix.Channels);
            properties.Add("m_nMixDownRule", int.TryParse(submix.MixDownRule, CultureInfo.InvariantCulture, out var rule) ? rule : 0);
            properties.Add("m_sendOperator", submix.SendOperator);

            for (var k = 0; k < 4; k++)
            {
                properties.Add(string.Create(CultureInfo.InvariantCulture, $"m_Send{k + 1}"), k < submix.Sends.Length ? submix.Sends[k] : string.Empty);
            }

            return properties;
        }

        private int SubmixMeter(int i, MixCommand command)
        {
            var name = MeterName(command);

            if (name != null && !name.Contains('/', StringComparison.Ordinal) && Next(i + 1, MixOp.ControlCopy) != null && Next(i + 2, MixOp.ControlSineBlend) != null)
            {
                return BlendBlock(i);
            }

            if (InnerBoundary(name, command.InputSubmix0))
            {
                return i + 1;
            }

            if (Split(name) is var (node, plug))
            {
                submixHolders[command.InputSubmix0] = new(node, plug);
            }

            return i + 1;
        }

        /// <summary>
        /// An audio meter passes its input through; it compiles to a meter and a spectrum of the submix it reads.
        /// </summary>
        private void AudioMeter(int i, string name, int submix)
        {
            if (submixHolders.TryGetValue(submix, out var holder) && holder.Node == name)
            {
                return;
            }

            var node = Node(name, "audio.Meter");

            if (i > 0 && commands[i - 1] is { Op: MixOp.SubmixMeter } meter && meter.InputSubmix0 == submix && MeterName(meter) is { } friendly)
            {
                node.FriendlyName = friendly;
            }

            Edge(node, "audio:In", submixHolders.TryGetValue(submix, out var source) ? source : null);
            submixHolders[submix] = new(name, "audio:Out");
        }

        /// <summary>
        /// The tracks and outputs of an inlined subgraph compile to nothing but meters ("subgraph/audio.Track1"),
        /// so a meter of one marks where audio enters or leaves the subgraph.
        /// </summary>
        private bool InnerBoundary(string? name, int submix)
        {
            if (name == null || name.LastIndexOf('/') is not (var slash and > 0))
            {
                return false;
            }

            var last = name[(slash + 1)..];
            var nodeName = name;

            if (last is "audio:In" or "audio:Source" or "audio:Out" && name.LastIndexOf('/', slash - 1) > 0)
            {
                nodeName = name[..slash];
            }
            else if (TypeOf(last) is not ("audio.Track" or "audio.Output"))
            {
                return false;
            }

            var type = TypeOf(nodeName);

            if (type is not ("audio.Track" or "audio.Output") || !nodeName.Contains('/', StringComparison.Ordinal))
            {
                return false;
            }

            if (submixHolders.TryGetValue(submix, out var holder) && holder.Node == nodeName)
            {
                return true;
            }

            var node = Node(nodeName, type);
            Edge(node, type == "audio.Track" ? "audio:In" : "audio:In1", submixHolders.TryGetValue(submix, out var source) ? source : null);
            submixHolders[submix] = new(nodeName, type == "audio.Track" ? "audio:Source" : "audio:Out");
            return true;
        }

        private (string? Name, string Type) ProcessorOwner(int i, int processor)
        {
            var type = graph.Processors[processor].NodeType;

            // The debug of the processed submix has the full name, prefixed when the node is inside an inlined subgraph
            for (var j = i + 1; j < Math.Min(i + 4, commands.Count); j++)
            {
                var command = commands[j];

                if (command.Op == MixOp.SubmixDebug && command.InputSubmix0 == commands[i].OutputSubmix)
                {
                    if (Split(NameOf(command.ParamName)) is var (node, _) && TypeOf(node) is { } debugNodeType && ProcessorType(type, debugNodeType) == debugNodeType)
                    {
                        return (node, debugNodeType);
                    }

                    break;
                }

                if (command.Op is MixOp.SubmixProcess or MixOp.SubmixGenerate)
                {
                    break;
                }
            }

            if (NameOf(commands[i].ParamName) is { } processName && !processName.Contains('/', StringComparison.Ordinal) && TypeOf(processName) is { } processType)
            {
                return (processName, ProcessorType(type, processType));
            }

            if (NameOf(graph.Processors[processor].DebugId) is { } debugName && TypeOf(debugName) is { } debugType)
            {
                return (debugName, ProcessorType(type, debugType));
            }

            return (null, type);
        }

        /// <summary>
        /// Gets the internal name of the first node of a type in a subgraph, when that graph was decompiled.
        /// </summary>
        public Func<string, string, string?> SubgraphNodes { get; init; } = static (_, _) => null;

        private string SubgraphNode(MixProcessor processor, string type)
            => processor.Subgraphs.Length > 0 && SubgraphNodes(processor.Subgraphs[0], type) is { } node ? node : string.Concat(type, "1");

        /// <summary>
        /// A subgraph switch exposes the tracks, outputs and controls of the first subgraph it lists, as "node (subgraph)".
        /// </summary>
        private static string SubgraphPlug(MixProcessor processor, string node)
            => processor.Subgraphs.Length > 0 ? $"{node} ({processor.Subgraphs[0]})" : node;

        private string SwitchOutputPlug(MixNode node, MixProcessor processor, uint hash)
        {
            for (var index = 1; index <= 64 && processor.Subgraphs.Length > 0; index++)
            {
                var output = string.Create(CultureInfo.InvariantCulture, $"audio.Output{index}");
                var plug = SubgraphPlug(processor, output);

                if (StringToken.Get($"{node.Name}/{plug}") == hash)
                {
                    return plug;
                }

                plug = DetailedSubgraphPlug(processor, output);

                if (StringToken.Get($"{node.Name}/{plug}") == hash)
                {
                    node.DetailedPlugs = true;
                    return plug;
                }
            }

            return SubgraphPlug(processor, SubgraphNode(processor, "audio.Output"));
        }

        /// <summary>The subgraphs a switch lists in the editor, without the default one the compiled list ends with.</summary>
        private static string[] EditorSubgraphs(MixProcessor processor) => processor.Subgraphs.Length > 1 ? processor.Subgraphs[..^1] : processor.Subgraphs;

        private static string DetailedSubgraphPlug(MixProcessor processor, string node)
            => string.Join(", \n", EditorSubgraphs(processor).Select(subgraph => $"{node} ({subgraph})"));

        /// <summary>
        /// Switches with detailed plug names name every plug after all of their subgraphs, and the selection plugs list them.
        /// </summary>
        private static void ApplyDetailedPlugNames(MixNode node)
        {
            var processor = node.Processor!;
            var subgraphs = EditorSubgraphs(processor);
            var selectionIndex = string.Concat("Selection Index", string.Concat(subgraphs.Select((subgraph, index) => string.Create(CultureInfo.InvariantCulture, $", {index}: {subgraph}"))));
            var selectionName = string.Concat("Selection Name ( ", string.Concat(subgraphs.Select(static subgraph => $"{subgraph}, ")), ")");
            var first = processor.Subgraphs.Length > 0 ? $" ({processor.Subgraphs[0]})" : string.Empty;

            for (var k = 0; k < node.Inputs.Count; k++)
            {
                var (plug, source) = node.Inputs[k];
                var renamed = plug switch
                {
                    "Selection Index" => selectionIndex,
                    "Selection Name" => selectionName,
                    _ when first.Length > 0 && plug.EndsWith(first, StringComparison.Ordinal) => DetailedSubgraphPlug(processor, plug[..^first.Length]),
                    _ => plug,
                };
                node.Inputs[k] = new(renamed, source);
            }
        }

        /// <summary>A node type sharing a processor with the one it was compiled from: limiters are compressors.</summary>
        private static string ProcessorType(string processorType, string? nodeType) => nodeType switch
        {
            null => processorType,
            "audio.Limiter" when processorType == "audio.Compressor" => nodeType,
            _ when nodeType == processorType => nodeType,
            "sys.Subgraph" or "audio.EffectChain" or "audio.Processor" => nodeType,
            _ => processorType,
        };

        private int ProcessorBlock(int i)
        {
            var pre = new List<MixCommand>();
            var j = i;

            while (j < commands.Count && commands[j].Op is MixOp.ProcessorSetControlValue or MixOp.ProcessorSetNameInput or MixOp.ProcessorSetVsndValue
                or MixOp.ProcessorSetControlArrayValue or MixOp.SubmixGenerateSidechain or MixOp.SubmixMeterSpectrum or MixOp.ProcessorSetImpulseResponseValue)
            {
                pre.Add(commands[j]);
                j++;
            }

            if (j >= commands.Count || commands[j].Op is not (MixOp.SubmixProcess or MixOp.SubmixGenerate))
            {
                Warnings.Add($"processor block at {i} has no process command");
                return i + 1;
            }

            var process = commands[j];
            var processorIndex = process.Processor;

            if (processorIndex < 0 || processorIndex >= graph.Processors.Count)
            {
                Warnings.Add($"process command {j} names processor {processorIndex}");
                return j + 1;
            }

            var processor = graph.Processors[processorIndex];
            var (name, type) = ProcessorOwner(j, processorIndex);
            name ??= TypeOf(processor.Name) == type ? processor.Name : Fresh(type);

            if (nodes.TryGetValue(name, out var existing) && existing.Processor != null && existing.Processor != processor)
            {
                Warnings.Add($"two processors share the name {name}");
                name = Fresh(type);
            }

            var node = Node(name, type);
            node.Type = type;
            node.FriendlyName = processor.Name;
            node.Processor = processor;
            processorNodes[processorIndex] = node;

            if (process.Op == MixOp.SubmixProcess)
            {
                var input = type switch
                {
                    "audio.Vocoder" => "audio:CarrierIn",
                    "sys.SubgraphSwitch" => SubgraphPlug(processor, SubgraphNode(processor, "audio.Track")),
                    _ => "audio:In",
                };
                Edge(node, input, submixHolders.TryGetValue(process.InputSubmix0, out var holder) ? holder : null);
            }

            foreach (var command in pre)
            {
                SetProcessorInput(node, command);
            }

            BindParameters(node, processor);

            j++;
            var output = process.OutputSubmix;

            while (j < commands.Count)
            {
                var command = commands[j];

                if (command.Op == MixOp.SubmixDebug && command.InputSubmix0 == output)
                {
                    var debug = Split(NameOf(command.ParamName));
                    submixHolders[output] = debug is var (debugNode, debugPlug) && debugNode == name
                        ? new(name, debugPlug)
                        : new(name, type == "sys.SubgraphSwitch" ? SwitchOutputPlug(node, processor, command.ParamName) : "audio:Out");
                }
                else if (command.Op == MixOp.SubmixMeterSpectrum && command.InputSubmix0 == output)
                {
                    if (Next(j + 1, MixOp.SubmixProcess) is { } nextProcess && nextProcess.InputSubmix0 == command.InputSubmix0 && Split(NameOf(command.ParamName)) is var (_, spectrumPlug) && spectrumPlug == "audio:In")
                    {
                        break;
                    }
                }
                else if (command.Op == MixOp.ProcessorStoreControlValue && command.Processor == processorIndex)
                {
                    valueSources[command.Value0] = new(name, StorePlug(command.ParamName));
                }
                else if (command.Op == MixOp.SubmixMeter && command.InputSubmix0 == output && MeterName(command) == processor.Name)
                {
                }
                else
                {
                    break;
                }

                j++;
            }

            if (!submixHolders.TryGetValue(output, out var current) || current.Node != name)
            {
                submixHolders[output] = new(name, type == "sys.SubgraphSwitch" ? SubgraphPlug(processor, SubgraphNode(processor, "audio.Output")) : "audio:Out");
            }

            return j;
        }

        private void SetProcessorInput(MixNode node, MixCommand command)
        {
            switch (command.Op)
            {
                case MixOp.ProcessorSetControlValue:
                {
                    var plug = ParameterPlugByHash(node.Type, command.ParamName) ?? NameOf(command.ParamName) ?? command.ParamName.ToString(CultureInfo.InvariantCulture);
                    node.ControlValues[plug] = command.Value0;

                    if (Source(command.Value0) is { } source)
                    {
                        Edge(node, plug, source);
                    }

                    break;
                }

                case MixOp.ProcessorSetControlArrayValue:
                {
                    var plug = ParameterPlugByHash(node.Type, command.ParamName) ?? NameOf(command.ParamName) ?? "array";

                    if (ArrayInput(command.Value0) is { } arrayNode)
                    {
                        Edge(node, plug, new(arrayNode.Name, "value"));
                    }

                    break;
                }

                case MixOp.ProcessorSetNameInput:
                    Edge(node, node.Type == "sys.SubgraphSwitch" ? "Selection Name" : "effect", new(EffectInput(command.Value0).Name, "Effect Name"));
                    break;

                case MixOp.ProcessorSetVsndValue:
                    Edge(node, "vsnd:ImpulseResponse", new(VsndInput(command.Value0).Name, "Vsnd Name"));
                    break;

                case MixOp.SubmixGenerateSidechain:
                    Edge(node, node.Type == "audio.AutoFilter" ? "audio:sidechain" : "audio.SideChain",
                        submixHolders.TryGetValue(command.InputSubmix0, out var holder) ? holder : null);
                    break;
            }
        }

        private static string? ParameterPlugByHash(string type, uint hash)
            // Some compilers hash the plug name with underscores for spaces, as in selection_index
            => NodeTypesByName.TryGetValue(type, out var nodeType)
                ? nodeType.Inputs.Concat(nodeType.LegacyPlugs).Concat(type == "sys.SubgraphSwitch" ? ["Selection Index"] : [])
                    .FirstOrDefault(plug => StringToken.Get(plug) == hash || StringToken.Get(plug.Replace(' ', '_')) == hash)
                : null;

        private static readonly Dictionary<string, string> ParameterAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CutoffFreq"] = "Frequency",
            ["CutoffFrequency"] = "Frequency",
            ["EQScale"] = "dynamicEQScale",
            ["ArrayPathingEQ"] = "pathingeq",
            ["ArrayPathingCoefficients"] = "pathingcoeffs",
            ["ImpulseResponse"] = "vsnd:ImpulseResponse",
            ["EffectName"] = "effect",
            ["Level"] = "LevelOut",
            ["dBLevel"] = "dBLevelOut",
            ["Reduction"] = "ReductionOut",
            ["Drive"] = "drive",
            ["PitchScale"] = "pitchScale",
        };

        private static string ParameterPlug(string type, string parameter)
        {
            if (ParameterAliases.TryGetValue(parameter, out var alias))
            {
                return type == "sys.SubgraphSwitch" && alias == "effect" ? "Selection Name" : alias;
            }

            static string Flat(string name) => name.Replace(".", string.Empty, StringComparison.Ordinal).Replace(":", string.Empty, StringComparison.Ordinal);

            var flat = Flat(parameter);

            if (NodeTypesByName.TryGetValue(type, out var nodeType))
            {
                foreach (var plug in nodeType.Inputs.Concat(nodeType.Outputs))
                {
                    if (string.Equals(Flat(plug), flat, StringComparison.OrdinalIgnoreCase))
                    {
                        return plug;
                    }
                }
            }

            return type == "sys.SubgraphSwitch" && parameter == "SelectionIndex" ? "Selection Index" : parameter;
        }

        /// <summary>
        /// Heap graphs bind processor parameters to values directly: an output writes its value,
        /// an input reads whatever node wrote the value it is bound to.
        /// </summary>
        private void BindParameters(MixNode node, MixProcessor processor)
        {
            foreach (var (parameter, value) in processor.Parameters)
            {
                var plug = ParameterPlug(node.Type, parameter);

                if (plug == "effect" || plug == "Selection Name")
                {
                    if (graph.NameInputs.FindIndex(input => input.Processor == value) is var nameInput and >= 0)
                    {
                        Edge(node, plug, new(EffectInput(nameInput).Name, "Effect Name"));
                    }

                    continue;
                }

                if (IsOutputPlug(node.Type, plug))
                {
                    valueSources[value] = new(node.Name, plug);
                    continue;
                }

                node.ControlValues[plug] = value;

                if (Source(value) is { } source)
                {
                    Edge(node, plug, source);
                }
            }

            foreach (var (parameter, input) in processor.InputParameters)
            {
                if (graph.VsndInputs.FindIndex(vsnd => vsnd.Processor == input) is var vsndInput and >= 0)
                {
                    Edge(node, ParameterPlug(node.Type, parameter), new(VsndInput(vsndInput).Name, "Vsnd Name"));
                }
                else if (graph.ArrayInputs.IndexOf(input) is var arrayInput and >= 0 && ArrayInput(arrayInput) is { } arrayNode)
                {
                    Edge(node, ParameterPlug(node.Type, parameter), new(arrayNode.Name, "value"));
                }
            }
        }

        private MixNode? ArrayInput(int index)
        {
            if (index < 0 || index >= graph.ControlInputArrays.Count)
            {
                return null;
            }

            if (inputNodes.TryGetValue(-20000 - index, out var existing))
            {
                return nodes[existing];
            }

            var array = graph.ControlInputArrays[index];
            var name = TypeOf(array.Name) == "control.InputArray" ? array.Name : Fresh("control.InputArray");
            var node = Node(name, "control.InputArray");
            node.FriendlyName = array.Name;
            node.Properties = KVObject.Collection();
            var data = array.Value >= 0 && array.Value < graph.ValueArrays.Count ? graph.ValueArrays[array.Value] : [];
            node.Properties.Add("m_vflData", KVObject.Array(data.Select(static value => (KVObject)value)));
            inputNodes[-20000 - index] = name;
            return node;
        }

        private MixNode EffectInput(int index)
        {
            if (inputNodes.TryGetValue(-1 - index, out var existing))
            {
                return nodes[existing];
            }

            var input = index >= 0 && index < graph.NameInputs.Count ? graph.NameInputs[index] : new MixStringInput("effect", "core.null", -1);
            var key = graph.Format == MixGraphFormat.Heap ? input.Processor : index;
            string? name = null;

            foreach (var meter in graph.NameInputMeters)
            {
                if (meter.Value == key && Split(meter.Name) is var (meterNode, _))
                {
                    name = meterNode;
                }
            }

            name ??= TypeOf(input.Name) == "effect.Input" ? input.Name : Fresh("effect.Input");
            var node = Node(name, "effect.Input");
            node.FriendlyName = input.Name;
            node.Properties = KVObject.Collection();
            node.Properties.Add("m_defaultValue", input.DefaultValue.Length > 0 ? input.DefaultValue : "core.null");
            inputNodes[-1 - index] = name;
            return node;
        }

        private MixNode VsndInput(int index)
        {
            if (inputNodes.TryGetValue(-10000 - index, out var existing))
            {
                return nodes[existing];
            }

            var input = graph.VsndInputs[index];
            var name = TypeOf(input.Name) == "vsnd.Input" ? input.Name : Fresh("vsnd.Input");
            var node = Node(name, "vsnd.Input");
            node.FriendlyName = input.Name;
            node.Properties = KVObject.Collection();
            node.Properties.Add("m_defaultValue", input.DefaultValue);
            inputNodes[-10000 - index] = name;
            return node;
        }

        private int SubmixCopy(int i, MixCommand command)
        {
            var next = i + 1 < commands.Count ? commands[i + 1] : (MixCommand?)null;

            if (command.Op == MixOp.SubmixAccumulate)
            {
                return AccumulateBlock(i);
            }

            if (command.OutputSubmix != command.InputSubmix0 && next is { Op: MixOp.SubmixMeter } meter
                && MeterName(meter) is { } meterName && !meterName.Contains('/', StringComparison.Ordinal) && meter.InputSubmix0 == command.OutputSubmix)
            {
                if (TypeOf(meterName) != "audio.Splitter")
                {
                    return SendAmpBlock(i);
                }
            }

            if (next is { Op: MixOp.ControlCopy } controlCopy && controlCopy.Value0 == command.Value0 && controlCopy.Value1 == command.Value1 && command.Value1 >= 0)
            {
                return AmpBlock(i);
            }

            if (next is { Op: MixOp.SubmixMeter } splitMeter && Split(MeterName(splitMeter)) is var (node, _) && TypeOf(node) == "audio.Splitter")
            {
                return SplitterBlock(i);
            }

            return MixBlock(i);
        }

        private int AccumulateBlock(int i)
        {
            var command = commands[i];
            var holder = submixHolders.TryGetValue(command.OutputSubmix, out var current) ? current : (PlugSource?)null;
            var node = holder is { } held && nodes.TryGetValue(held.Node, out var heldNode) && heldNode.IsAccumulatingMix ? heldNode : null;

            if (node == null)
            {
                // Older graphs meter the mix after its last input, under the mix's own name
                var j = i;

                while (j < commands.Count && commands[j].Op == MixOp.SubmixAccumulate && commands[j].OutputSubmix == command.OutputSubmix)
                {
                    j++;
                }

                var metered = Next(j, MixOp.SubmixMeter) is { } meter && meter.InputSubmix0 == command.OutputSubmix
                    && MeterName(meter) is { } meterName && TypeOf(meterName) == "audio.Mix" && !nodes.ContainsKey(meterName) ? meterName : null;

                node = Node(metered ?? Fresh("audio.Mix"), "audio.Mix");
                node.IsAccumulatingMix = true;
                node.FriendlyName = metered ?? string.Concat(holder is { } source && nodes.TryGetValue(source.Node, out var sourceNode) ? sourceNode.FriendlyName ?? source.Node : "mix", "_sum");
                node.Properties.Add("m_flVolume1", 1.0f);
                Edge(node, "audio:In1", holder);
                Warnings.Add($"{node.Name}: accumulate at {i} outside a mix, modelled as a mix");
            }

            // The control meters "MixN/volumeK" tell which input this is, as inputs may be left unconnected
            var k = meteredInputs
                .Where(pair => pair.Key.Node == node.Name && pair.Value == command.Value0 && pair.Key.Plug.StartsWith("volume", StringComparison.Ordinal))
                .Select(static pair => int.TryParse(pair.Key.Plug.AsSpan(6), CultureInfo.InvariantCulture, out var index) ? index : 0)
                .FirstOrDefault(index => index > 0 && !node.HasInput(Indexed("audio:In", index)));

            if (k == 0)
            {
                k = 1 + node.Inputs.Count(static input => input.Key.StartsWith("audio:In", StringComparison.Ordinal));
            }

            Edge(node, Indexed("audio:In", k), submixHolders.TryGetValue(command.InputSubmix0, out var input) ? input : null);
            SetVolume(node, Indexed("volume", k), Indexed("m_flVolume", k), command.Value0);
            submixHolders[command.OutputSubmix] = new(node.Name, "audio:Out");
            return i + 1;
        }

        private static string Indexed(string prefix, int index) => string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}");

        private void SetVolume(MixNode node, string plug, string property, int value)
        {
            if (Source(value) is { } source)
            {
                Edge(node, plug, source);
                node.Properties[property] = 1.0f;
            }
            else
            {
                node.Properties[property] = graph.Value(value);
            }
        }

        private int SendAmpBlock(int i)
        {
            var command = commands[i];
            var meter = commands[i + 1];
            var node = Node(Fresh("audio.Amp"), "audio.Amp");
            node.FriendlyName = MeterName(meter);
            Edge(node, "audio:In", submixHolders.TryGetValue(command.InputSubmix0, out var holder) ? holder : null);
            SetVolume(node, "volume", "m_flVolume", command.Value0);
            submixHolders[command.OutputSubmix] = new(node.Name, "audio:Out");
            Warnings.Add($"{node.Name}: copy at {i} outside a splitter, modelled as an amp");
            return i + 2;
        }

        private int AmpBlock(int i)
        {
            var command = commands[i];
            var submix = command.OutputSubmix;
            var source = submixHolders.TryGetValue(command.InputSubmix0, out var holder) ? holder : (PlugSource?)null;
            var j = i + 2;
            string? name = null;

            if (Next(j, MixOp.SubmixDebug) is { } debug)
            {
                name = Split(NameOf(debug.ParamName))?.Node;
                j++;
            }

            name ??= meteredInputs.FirstOrDefault(pair => pair.Value == command.Value1 && pair.Key.Plug == "volume" && TypeOf(pair.Key.Node) == "audio.Amp").Key.Node;
            name ??= Fresh("audio.Amp");
            var node = Node(name, "audio.Amp");
            Edge(node, "audio:In", source);
            SetVolume(node, "volume", "m_flVolume", command.Value1);

            if (Next(j, MixOp.SubmixMeter) is { } meter && meter.InputSubmix0 == submix)
            {
                node.FriendlyName = MeterName(meter);
                j++;
            }

            submixHolders[submix] = new(name, "audio:Out");
            return j;
        }

        private int SplitterBlock(int i)
        {
            var j = i;
            var submix = commands[i].Op == MixOp.ControlCondCopyIfNegative ? commands[i + 1].InputSubmix0 : commands[i].InputSubmix0;
            var source = submixHolders.TryGetValue(submix, out var holder) ? holder : (PlugSource?)null;
            var outputs = new List<(string Plug, int Submix, int Value)>();
            string? name = null;

            while (j + 1 < commands.Count && commands[j].Op == MixOp.SubmixCopy && commands[j + 1].Op == MixOp.SubmixMeter)
            {
                if (Split(MeterName(commands[j + 1])) is not var (node, plug) || (name != null && node != name))
                {
                    break;
                }

                name = node;
                outputs.Add((plug, commands[j].OutputSubmix, commands[j].Value0));
                j += 2;
            }

            // The output that keeps the input's submix ramps its volume in place, like an amp
            if (Next(j, MixOp.ControlCondCopyIfNegative) is { } ramp && Next(j + 1, MixOp.SubmixCopy) is { } inPlace && inPlace.OutputSubmix == submix
                && inPlace.InputSubmix0 == submix && Next(j + 2, MixOp.ControlCopy) != null && Next(j + 3, MixOp.SubmixMeter) is { } rampMeter
                && Split(MeterName(rampMeter)) is var (rampNode, rampPlug) && (name == null || rampNode == name) && ramp.Value1 == inPlace.Value1)
            {
                name ??= rampNode;
                outputs.Add((rampPlug, submix, inPlace.Value1));
                j += 4;
            }

            while (j < commands.Count && commands[j].Op == MixOp.SubmixMeter && Split(MeterName(commands[j])) is var (aliasNode, aliasPlug) && aliasNode == name)
            {
                outputs.Add((aliasPlug, commands[j].InputSubmix0, -1));
                j++;
            }

            var splitter = Node(name!, "audio.Splitter");
            Edge(splitter, "audio:In", source);
            var lostVolumes = new List<int>();

            foreach (var (plug, outputSubmix, value) in outputs)
            {
                if (value >= 0 && int.TryParse(plug.AsSpan(plug.Length - 1), CultureInfo.InvariantCulture, out var k))
                {
                    // A connected volume of a copied output keeps only its ramp state, which starts at -1
                    if (outputSubmix != submix && Source(value) == null && graph.Value(value) == -1f)
                    {
                        lostVolumes.Add(k);
                    }
                    else
                    {
                        SetVolume(splitter, Indexed("volume", k), Indexed("m_flVolume", k), value);
                    }
                }

                submixHolders[outputSubmix] = new(name!, plug);
            }

            foreach (var k in lostVolumes)
            {
                // The other volumes are usually the complementary output of the same control
                var partner = splitter.Inputs.Where(static input => input.Key.StartsWith("volume", StringComparison.Ordinal)).Select(static input => input.Value).FirstOrDefault();
                var recovered = partner.Plug switch
                {
                    "inverseOut" => partner with { Plug = "valueOut" },
                    "valueOut" => partner with { Plug = "inverseOut" },
                    "fadeInValue" => partner with { Plug = "fadeOutValue" },
                    "fadeOutValue" => partner with { Plug = "fadeInValue" },
                    _ => partner,
                };

                if (recovered.Node != null)
                {
                    splitter.SetInput(Indexed("volume", k), recovered);
                    splitter.Properties[Indexed("m_flVolume", k)] = 1.0f;
                    Warnings.Add($"{name}/volume{k} lost its source when compiled, connected to {recovered.Node}/{recovered.Plug}");
                }
                else
                {
                    splitter.Properties[Indexed("m_flVolume", k)] = 1.0f;
                    Warnings.Add($"{name}/volume{k} lost its source when compiled");
                }
            }

            if (Next(j, MixOp.SubmixMeter) is { } meter && meter.InputSubmix0 == submix && MeterName(meter) is { } meterName && !meterName.Contains('/', StringComparison.Ordinal))
            {
                splitter.FriendlyName = meterName;
                j++;
            }

            foreach (var (_, outputSubmix, _) in outputs)
            {
                if (outputSubmix != submix && outputSubmix < graph.Submixes.Count)
                {
                    splitter.FriendlyName ??= graph.Submixes[outputSubmix].Name;
                }
            }

            return j;
        }

        /// <summary>
        /// Each output of a splitter blend is a blend factor (copy, sine blend) applied to copies of the input, then metered.
        /// </summary>
        private int SplitterBlendBlock(int i)
        {
            var j = i;
            var source = (PlugSource?)null;
            string? name = null;
            var parameters = -1;
            var blend = -1;
            var outputs = new List<(string Plug, int Submix)>();

            while (j + 2 < commands.Count && commands[j].Op == MixOp.ControlCopy && commands[j + 1].Op == MixOp.ControlSineBlend && commands[j + 2].Op == MixOp.SubmixCopy)
            {
                var k = j + 2;
                var copy = commands[k];

                while (k < commands.Count && commands[k].Op == MixOp.SubmixCopy)
                {
                    k++;
                }

                if (Next(k, MixOp.SubmixMeter) is not { } meter || Split(MeterName(meter)) is not var (node, plug) || TypeOf(node) != "audio.SplitterBlend" || (name != null && node != name))
                {
                    break;
                }

                name = node;
                blend = commands[j].Value1;
                parameters = commands[j + 1].Value1;
                source ??= submixHolders.TryGetValue(copy.InputSubmix0, out var holder) ? holder : null;
                outputs.Add((plug, copy.OutputSubmix));
                j = k + 1;
            }

            if (name == null)
            {
                return ControlBlock(i);
            }

            var splitter = Node(name, "audio.SplitterBlend");
            splitter.Properties.Add("m_flLockAmount", graph.Value(parameters + 1) * 2.0f);
            Edge(splitter, "audio:In", source);
            Edge(splitter, "blend", Source(blend));

            foreach (var (plug, submix) in outputs)
            {
                submixHolders[submix] = new(name, plug);
            }

            return j;
        }

        private int BlendBlock(int i)
        {
            var meter = commands[i];
            var submix = meter.InputSubmix0;
            var friendly = MeterName(meter);
            var j = i + 1;
            var parts = new List<(MixCommand Copy, MixCommand Blend, MixCommand Mix)>();

            while (j + 2 < commands.Count && commands[j].Op == MixOp.ControlCopy && commands[j + 1].Op == MixOp.ControlSineBlend
                && commands[j + 2].Op is MixOp.SubmixCopy or MixOp.SubmixAccumulate)
            {
                parts.Add((commands[j], commands[j + 1], commands[j + 2]));
                j += 3;
            }

            if (parts.Count == 0)
            {
                return i + 1;
            }

            var name = meteredInputs.FirstOrDefault(pair => TypeOf(pair.Key.Node) == "audio.Blend" && pair.Value == parts[0].Copy.Value0).Key.Node ?? Fresh("audio.Blend");
            var node = Node(name, "audio.Blend");
            node.FriendlyName = friendly;
            node.Properties.Add("m_flLockAmount", graph.Value(parts[0].Blend.Value1 + 1) * 2.0f);
            Edge(node, "blend", Source(parts[0].Copy.Value1));

            for (var k = 0; k < parts.Count; k++)
            {
                var copy = parts[k].Copy;
                var plug = meteredInputs.FirstOrDefault(pair => pair.Key.Node == name && pair.Value == copy.Value0).Key.Plug ?? Indexed("audio:In", k + 1);
                Edge(node, plug, submixHolders.TryGetValue(parts[k].Mix.InputSubmix0, out var holder) ? holder : null);
            }

            submixHolders[submix] = new(name, "audio:Out");
            return j;
        }

        private int MixBlock(int i)
        {
            var command = commands[i];
            var submix = command.OutputSubmix;
            var parts = new List<MixCommand>();
            var j = i;

            while (j < commands.Count && commands[j].Op is MixOp.SubmixCopy or MixOp.SubmixAccumulate && commands[j].OutputSubmix == submix)
            {
                parts.Add(commands[j]);
                j++;
            }

            string? name = null;
            string? friendly = null;

            if (Next(j, MixOp.SubmixMeter) is { } meter && meter.InputSubmix0 == submix
                && Next(j + 1, MixOp.SubmixDebug) is { } meterDebug && Split(NameOf(meterDebug.ParamName)) is var (meterNode, _) && TypeOf(meterNode) == "audio.Mix")
            {
                name = meterNode;
                friendly = MeterName(meter);
                j += 2;
            }

            if (name == null && Next(j, MixOp.SubmixDebug) is { } debug && Split(NameOf(debug.ParamName)) is var (debugNode, _) && TypeOf(debugNode) == "audio.Mix")
            {
                name = debugNode;
                j++;
            }

            if (name == null)
            {
                name = Fresh("audio.Mix");
                Warnings.Add($"mix at {i} has no debug name, named {name}");
            }

            var node = Node(name, "audio.Mix");
            node.FriendlyName = friendly ?? node.FriendlyName;

            // The control meters "MixN/volumeK" tell which input each part is
            var volumes = new Dictionary<int, List<int>>();

            foreach (var ((meterNodeName, plug), value) in meteredInputs)
            {
                if (meterNodeName == name && plug.StartsWith("volume", StringComparison.Ordinal)
                    && int.TryParse(plug.AsSpan(6), CultureInfo.InvariantCulture, out var k))
                {
                    (volumes.TryGetValue(value, out var list) ? list : volumes[value] = []).Add(k);
                }
            }

            var used = new HashSet<int>();

            foreach (var part in parts)
            {
                var candidates = volumes.TryGetValue(part.Value0, out var list) ? list.Where(k => !used.Contains(k)).ToList() : [];
                var k = candidates.Count > 0 ? candidates[0] : Enumerable.Range(1, 8).First(index => !used.Contains(index));
                used.Add(k);

                Edge(node, Indexed("audio:In", k), submixHolders.TryGetValue(part.InputSubmix0, out var holder) ? holder : null);
                SetVolume(node, Indexed("volume", k), Indexed("m_flVolume", k), part.Value0);
            }

            submixHolders[submix] = new(name, "audio:Out");
            return j;
        }

        private int OutputBlock(int i, int[] submixes, int[] values, MixCommand command)
        {
            var j = i + 1;
            string? friendly = null;

            if (Next(j, MixOp.SubmixMeter) is { } meter)
            {
                friendly = MeterName(meter);
                j++;
            }

            // Connected volumes are metered as "audio.OutputN/volumeK", which names the output
            var metered = meteredInputs.FirstOrDefault(pair => TypeOf(pair.Key.Node) == "audio.Output" && !nodes.ContainsKey(pair.Key.Node)
                && pair.Key.Plug.StartsWith("volume", StringComparison.Ordinal)
                && int.TryParse(pair.Key.Plug.AsSpan(6), CultureInfo.InvariantCulture, out var k) && k >= 1 && k <= values.Length && values[k - 1] == pair.Value).Key.Node;
            var name = metered
                ?? (friendly != null && TypeOf(friendly) == "audio.Output" && !nodes.ContainsKey(friendly) ? friendly : Fresh("audio.Output"));
            var node = Node(name, "audio.Output");
            node.FriendlyName = friendly;
            node.Properties.Add("m_flVolume1", 1.0f);
            node.Properties.Add("m_flVolume2", 1.0f);
            // Outputs other than the main one send to the additional outputs, counted from 1
            var send = command.OutputSubmix - 1;
            node.Properties.Add("m_sendTo", send >= 0 && send < graph.AdditionalOutputs.Count ? graph.AdditionalOutputs[send] : string.Empty);

            for (var k = 0; k < submixes.Length; k++)
            {
                Edge(node, Indexed("audio:In", k + 1), submixHolders.TryGetValue(submixes[k], out var holder) ? holder : null);
                SetVolume(node, Indexed("volume", k + 1), Indexed("m_flVolume", k + 1), values[k]);
            }

            return j;
        }

        private void CreateGraphInputNodes()
        {
            foreach (var input in graph.ControlInputs)
            {
                if (input.Name.StartsWith("$auto:", StringComparison.Ordinal))
                {
                    var automatic = graph.AutomaticControlInputs.Find(auto => auto.Name == input.Name[6..]);
                    var listener = automatic != null && graph.Source.GetArray("m_automaticControlInputs") is { } autos
                        && autos.FirstOrDefault(auto => GetString(auto, "m_name") == automatic.Name) is { } source
                        && GetString(source, "m_nControlType").StartsWith("VMIX_AUTO_LISTENER", StringComparison.Ordinal);
                    var type = listener ? "control.Listener" : "control.Automatic";
                    var name = nodes.Keys.FirstOrDefault(key => TypeOf(key) == type) ?? Fresh(type);
                    var node = Node(name, type);
                    node.FriendlyName ??= listener ? "Listener" : "Auto";
                    valueSources[input.Value] = new(name, input.Name[6..]);
                    continue;
                }

                var inputName = MeteredOutputNode(input.Value, "control.Input", "value") ?? (TypeOf(input.Name) == "control.Input" ? input.Name : Fresh("control.Input"));
                var inputNode = Node(inputName, "control.Input");
                inputNode.FriendlyName = input.Name;
                inputNode.Properties = ControlInputProperties(input.DefaultValue, false);
                valueSources[input.Value] = new(inputName, "value");
            }
        }

        private string? MeteredOutputNode(int value, string type, string plug)
            => meteredOutputs.TryGetValue(value, out var outputs) ? outputs.Where(output => output.Plug == plug && TypeOf(output.Node) == type).Select(static output => output.Node).FirstOrDefault() : null;

        private static KVObject ControlInputProperties(float defaultValue, bool decibels)
        {
            var properties = KVObject.Collection();
            properties.Add("m_flDefaultValue", defaultValue);
            properties.Add("m_flMinRange", 0.0f);
            properties.Add("m_flMaxRange", 1.0f);
            properties.Add("m_bUseDecibels", decibels);
            return properties;
        }

        private int ControlStore(int i, MixCommand command)
        {
            var value = command.Value0;
            var index = command.Value1;

            if (index < 0 || index >= graph.ControlInputs.Count)
            {
                Warnings.Add($"control store {i} reads input {index}");
                return i + 1;
            }

            var input = graph.ControlInputs[index];

            if (input.Name.StartsWith("$auto:", StringComparison.Ordinal))
            {
                // One automatic node, each connected output plug is named after its variable
                var automaticName = nodes.Keys.FirstOrDefault(key => TypeOf(key) == "control.Automatic") ?? Fresh("control.Automatic");
                var automatic = Node(automaticName, "control.Automatic");
                automatic.FriendlyName ??= "Auto";
                valueSources[value] = new(automaticName, input.Name[6..]);
                return i + 1;
            }

            var name = MeteredOutputNode(value, "control.Input", "value") ?? Fresh("control.Input");
            var node = Node(name, "control.Input");
            node.FriendlyName = input.Name;
            node.Properties = ControlInputProperties(input.DefaultValue, command.Op == MixOp.ControlInputStoreDecibels);
            valueSources[value] = new(name, "value");
            controlStores.Add((i, value, name));
            return i + 1;
        }

        private int ControlBlock(int i)
        {
            var j = i;
            var copies = new List<MixCommand>();

            while (j < commands.Count && commands[j].Op == MixOp.ControlCopy)
            {
                copies.Add(commands[j]);
                j++;
            }

            var remaps = new List<MixCommand>();

            static bool IsRemap(MixOp op) => op is MixOp.ControlRemapLinear or MixOp.ControlRemapSine or MixOp.ControlRemapLogLinear or MixOp.ControlEvaluateCurve;

            while (j < commands.Count && IsRemap(commands[j].Op))
            {
                remaps.Add(commands[j]);
                j++;

                // Older graphs compile a second output as a copy of the first, remapped again in place
                if (j + 1 < commands.Count && commands[j].Op == MixOp.ControlCopy && commands[j + 1].Op == commands[j - 1].Op
                    && remaps.Any(remap => remap.Value0 == commands[j].Value1) && commands[j + 1].Value0 == commands[j].Value0)
                {
                    copies.Add(commands[j]);
                    j++;
                }
            }

            if (remaps.Count == 0)
            {
                // A copy into a value a processor reads: the value is an alias of its source
                foreach (var copy in copies)
                {
                    if (Source(copy.Value1) is { } source)
                    {
                        valueSources[copy.Value0] = source;
                    }
                }

                return j;
            }

            var outputs = remaps.Select(static remap => remap.Value0).ToHashSet();
            string? owner = null;
            var outputPlugs = new Dictionary<int, string>();

            foreach (var remap in remaps)
            {
                if (!meteredOutputs.TryGetValue(remap.Value0, out var metered))
                {
                    continue;
                }

                foreach (var (node, plug) in metered)
                {
                    if (TypeOf(node) is "control.Remap" or "control.Crossfade" or "control.Curve")
                    {
                        owner = node;
                        outputPlugs[remap.Value0] = plug;
                    }
                }
            }

            var op = remaps[0].Op;
            var type = op switch
            {
                MixOp.ControlRemapSine => "control.Crossfade",
                MixOp.ControlEvaluateCurve => "control.Curve",
                _ => "control.Remap",
            };

            if (owner != null && TypeOf(owner) is { } ownerType && ownerType != type)
            {
                type = ownerType;
            }

            owner ??= Fresh(type);
            var target = Node(owner, type);
            var firstOutput = remaps[0].Value0;
            int? sourceValue = null;

            foreach (var copy in copies)
            {
                var (destination, source) = (copy.Value0, copy.Value1);

                if (destination == firstOutput || (outputs.Contains(destination) && !outputs.Contains(source)))
                {
                    sourceValue ??= source;
                }
                else if (!outputs.Contains(destination))
                {
                    // A connected parameter input, copied into the remap's parameter block
                    var k = destination - remaps[0].Value1;
                    string?[] parameters = type == "control.Crossfade" ? ["fadeStart", "fadeEnd", null, null] : ["inputMin", "inputMax", "outputMin", "outputMax"];

                    if (type != "control.Curve" && k is >= 0 and < 4 && parameters[k] is { } plug)
                    {
                        if (Source(source) is { } parameterSource)
                        {
                            Edge(target, plug, parameterSource);
                        }
                    }
                    else if (Source(source) is { } aliased)
                    {
                        // A copy the next copies read from, which only passes the value on
                        valueSources[destination] = aliased;
                    }
                }
            }

            if (sourceValue is { } inputValue)
            {
                Edge(target, "valueIn", Source(inputValue));
            }

            var parametersStart = remaps[0].Value1;
            target.Properties = KVObject.Collection();

            switch (type)
            {
                case "control.Remap":
                {
                    var inverse = outputPlugs.GetValueOrDefault(remaps[0].Value0) == "inverseOut";
                    target.Properties.Add("m_flInputMin", graph.Value(parametersStart));
                    target.Properties.Add("m_flInputMax", graph.Value(parametersStart + 1));
                    target.Properties.Add("m_flOutputStart", graph.Value(parametersStart + (inverse ? 3 : 2)));
                    target.Properties.Add("m_flOutputEnd", graph.Value(parametersStart + (inverse ? 2 : 3)));
                    target.Properties.Add("m_flPower", op == MixOp.ControlRemapLogLinear ? graph.Value(parametersStart + 4) : 1.0f);
                    break;
                }

                case "control.Crossfade":
                    target.Properties.Add("m_flFadeStart", graph.Value(parametersStart));
                    target.Properties.Add("m_flFadeEnd", graph.Value(parametersStart + 1));
                    break;

                default:
                    // A curve used only for its inverse output is stored inverted
                    target.Properties = CurveProperties(parametersStart, outputPlugs.GetValueOrDefault(remaps[0].Value0) == "inverseOut");
                    break;
            }

            var defaultPlugs = type == "control.Crossfade" ? ("fadeInValue", "fadeOutValue") : ("valueOut", "inverseOut");

            for (var k = 0; k < remaps.Count; k++)
            {
                var plug = outputPlugs.GetValueOrDefault(remaps[k].Value0) ?? (k == 0 ? defaultPlugs.Item1 : defaultPlugs.Item2);
                valueSources[remaps[k].Value0] = new(owner, plug);
            }

            return j;
        }

        private KVObject CurveProperties(int curveIndex, bool inverted)
        {
            var spline = KVObject.Array();
            var tangents = KVObject.Array();

            // Heap graphs keep a curve on the heap: its point count, the offset of its points, then the points
            var (start, count) = graph.Format == MixGraphFormat.Heap
                ? (curveIndex + 2, curveIndex >= 0 && curveIndex < graph.RawValues.Length ? (int)graph.RawValues[curveIndex] : 0)
                : curveIndex >= 0 && curveIndex < graph.Curves.Count ? graph.Curves[curveIndex] : (0, 0);
            var points = graph.Format == MixGraphFormat.Heap ? graph.Values : graph.ControlPoints;

            for (var k = start; k + 3 < points.Length && k < start + 4 * count; k += 4)
            {
                var point = KVObject.Collection();
                var sign = inverted ? -1f : 1f;
                point.Add("x", points[k]);
                point.Add("y", inverted ? 1f - points[k + 1] : points[k + 1]);
                point.Add("m_flSlopeIncoming", sign * points[k + 2]);
                point.Add("m_flSlopeOutgoing", sign * points[k + 3]);
                spline.Add(point);

                var tangent = KVObject.Collection();
                // A point whose slopes differ was edited by hand, which only free tangents keep
                var tangentType = points[k + 2] == points[k + 3] ? "CURVE_TANGENT_SPLINE" : "CURVE_TANGENT_FREE";
                tangent.Add("m_nIncomingTangent", tangentType);
                tangent.Add("m_nOutgoingTangent", tangentType);
                tangents.Add(tangent);
            }

            var curve = KVObject.Collection();
            curve.Add("m_spline", spline);
            curve.Add("m_tangents", tangents);
            curve.Add("m_vDomainMins", KVObject.Array([0.0f, 0.0f]));
            curve.Add("m_vDomainMaxs", KVObject.Array([1.0f, 1.0f]));

            var properties = KVObject.Collection();
            properties.Add("m_flInputMin", 0.0f);
            properties.Add("m_flInputMax", 1.0f);
            properties.Add("m_flOutputMin", 0.0f);
            properties.Add("m_flOutputMax", 1.0f);
            properties.Add("m_curve", curve);
            return properties;
        }

        private int ControlMax(int i, MixCommand command)
        {
            var name = meteredOutputs.TryGetValue(command.OutputSubmix, out var outputs)
                ? outputs.Where(static output => TypeOf(output.Node) == "control.Max").Select(static output => output.Node).FirstOrDefault()
                : null;
            name ??= Fresh("control.Max");
            var node = Node(name, "control.Max");

            // Heap graphs pack the last two offsets into the processor and parameter fields, where 0 is unused
            static int Packed(uint value) => value == 0 ? -1 : (int)(value & 0x7FFFF);
            var heap = graph.Format == MixGraphFormat.Heap;
            int[] operands =
            [
                command.Value0, command.Value1, command.InputSubmix0, command.InputSubmix1,
                heap ? Packed(command.ParamName) : command.ParamName == 0 ? -1 : unchecked((int)command.ParamName),
                heap ? Packed(unchecked((uint)command.Processor)) : command.Processor,
            ];

            for (var k = 0; k < operands.Length; k++)
            {
                if (operands[k] >= 0)
                {
                    Edge(node, Indexed("value", k + 1), Source(operands[k]));
                }
            }

            valueSources[command.OutputSubmix] = new(name, "valueOut");
            return i + 1;
        }

        private int ControlOutput(int i, MixCommand command)
        {
            var (outputIndex, value) = graph.Format == MixGraphFormat.Heap ? (command.Value0, command.Value1) : (command.Value1, command.Value0);
            var output = outputIndex >= 0 && outputIndex < graph.ControlOutputs.Count ? graph.ControlOutputs[outputIndex] : null;
            var name = meteredInputs.FirstOrDefault(pair => pair.Value == value && pair.Key.Plug == "value" && TypeOf(pair.Key.Node) == "control.Output").Key.Node
                ?? (output != null && TypeOf(output.Name) == "control.Output" ? output.Name : Fresh("control.Output"));
            var node = Node(name, "control.Output");
            node.FriendlyName = output?.Name;
            node.Properties = KVObject.Collection();
            node.Properties.Add("m_flDefaultValue", output?.DefaultValue ?? 1.0f);
            Edge(node, "value", Source(value));
            return i + 1;
        }

        private int TransientInput(int i, MixCommand command)
        {
            var index = command.Value1;
            var input = index >= 0 && index < graph.TransientInputs.Count ? graph.TransientInputs[index] : null;
            var name = input != null && TypeOf(input.Name) == "control.InputBang" ? input.Name : Fresh("control.InputBang");
            var node = Node(name, "control.InputBang");
            node.FriendlyName = input?.Name;
            valueSources[command.Value0] = new(name, "value");
            return i + 1;
        }

        private int Envelope(int i, MixCommand command)
        {
            var name = NameOf(command.ParamName) is { } named && TypeOf(named) == "control.AHREnvelope" ? named
                : MeteredOutputNode(command.OutputSubmix, "control.AHREnvelope", "out") ?? Fresh("control.AHREnvelope");
            var node = Node(name, "control.AHREnvelope");

            // The trigger reaches the envelope through its timer, which a reset command restarts from the trigger value
            for (var j = i - 1; j >= 0 && j >= i - 4; j--)
            {
                if (commands[j].Op == MixOp.ControlResetTimer && commands[j].Value0 == command.Value0)
                {
                    Edge(node, "trigger", Source(commands[j].Value1));
                    break;
                }
            }

            // The envelope keeps its settings in the command itself, as the bits of its operands
            static float Bits(uint value) => BitConverter.UInt32BitsToSingle(value);
            node.Properties = KVObject.Collection();
            node.Properties.Add("m_flBaseValue", Bits(command.Raw.InputSubmix0));
            node.Properties.Add("m_flDestinationValue", Bits(command.Raw.InputSubmix1));
            node.Properties.Add("m_flAttackTime", Bits(command.Raw.Value1));
            node.Properties.Add("m_flHoldTime", Bits(unchecked((uint)command.Processor)));
            node.Properties.Add("m_flReleaseTime", Bits(command.ParamName));
            valueSources[command.OutputSubmix] = new(name, "out");
            return i + 1;
        }

        /// <summary>
        /// Finds the graph an inlined subgraph was compiled from, given the names its nodes had there.
        /// </summary>
        public Func<IReadOnlyCollection<string>, string?> FindSubgraph { get; init; } = static _ => null;

        /// <summary>
        /// The outputs of subgraphs as graphs that inline them name them: the graph, what the output reads there, and its name.
        /// </summary>
        public List<(string Graph, PlugSource Source, string Output)> SubgraphOutputs { get; } = [];

        /// <summary>
        /// Gets the name graphs that inline this graph give the output that reads a node plug, when they do.
        /// </summary>
        public Func<PlugSource, string?> OutputName { get; init; } = static _ => null;

        /// <summary>
        /// Outputs keep no internal name when their meter has a friendly one, but graphs that inline this one
        /// name them in their plugs, so they get those names back.
        /// </summary>
        private void RenameOutputs()
        {
            foreach (var node in nodes.Values.Where(static node => node.Type == "audio.Output").ToList())
            {
                if (node.Inputs.FirstOrDefault().Value is not { Node: not null } source || OutputName(source) is not { } name || name == node.Name)
                {
                    continue;
                }

                // Swap names with an output that already has the wanted one
                var oldName = node.Name;
                var temporary = string.Concat(oldName, "#");
                RenameNode(oldName, temporary);

                if (nodes.ContainsKey(name))
                {
                    RenameNode(name, oldName);
                }

                RenameNode(temporary, name);
            }
        }

        private void RenameNode(string oldName, string newName)
        {
            var index = nodes.IndexOf(oldName);
            var node = nodes[oldName];
            nodes.RemoveAt(index);
            node.Name = newName;
            nodes.Insert(index, newName, node);

            foreach (var other in nodes.Values)
            {
                for (var k = 0; k < other.Inputs.Count; k++)
                {
                    if (other.Inputs[k].Value.Node == oldName)
                    {
                        other.Inputs[k] = new(other.Inputs[k].Key, other.Inputs[k].Value with { Node = newName });
                    }
                }
            }
        }

        /// <summary>
        /// Replaces the nodes of each inlined subgraph with one subgraph node, keeping only the edges that cross into it or out of it.
        /// </summary>
        private void CollapseSubgraphs()
        {
            var groups = nodes.Keys.Where(static name => name.Contains('/', StringComparison.Ordinal))
                .GroupBy(static name => name[..name.IndexOf('/', StringComparison.Ordinal)], StringComparer.Ordinal)
                .ToList();

            if (groups.Count == 0)
            {
                return;
            }

            var valueOf = new Dictionary<PlugSource, int>();

            foreach (var (value, source) in valueSources)
            {
                valueOf.TryAdd(source, value);
            }

            foreach (var group in groups)
            {
                var prefix = group.Key;
                var inner = group.ToHashSet(StringComparer.Ordinal);
                bool IsInner(string node) => node.StartsWith(prefix + "/", StringComparison.Ordinal);
                string Short(string node) => node[(prefix.Length + 1)..];

                var subgraphName = TypeOf(prefix) == "sys.Subgraph" && !nodes.ContainsKey(prefix) ? prefix : Fresh("sys.Subgraph");
                var subgraph = Node(subgraphName, "sys.Subgraph");
                subgraph.FriendlyName = prefix;

                var innerNames = inner.Select(Short)
                    .Concat(inner.Select(name => nodes[name].FriendlyName).Where(static friendly => friendly != null).Select(static friendly => string.Concat("=", friendly)))
                    .ToList();
                var graphName = FindSubgraph(innerNames);

                if (graphName == null)
                {
                    Warnings.Add($"no graph in this file has the nodes of the inlined subgraph {prefix}");
                }

                subgraph.Properties = KVObject.Collection();
                subgraph.Properties.Add("subgraphFile", graphName != null ? SubgraphFile(graphName) : string.Empty);
                subgraph.Properties.Add("subgraphName", graphName ?? string.Empty);

                // Control inputs of the subgraph alias the value fed to them
                var controlInputs = new Dictionary<int, string>();

                foreach (var meter in graph.ControlMeters)
                {
                    if (Split(meter.Name) is (var meterNode, "value:Out") && IsInner(meterNode) && TypeOf(meterNode) == "control.Input")
                    {
                        controlInputs.TryAdd(meter.Value, Short(meterNode));
                    }
                }

                foreach (var name in inner)
                {
                    foreach (var (plug, source) in nodes[name].Inputs)
                    {
                        if (IsInner(source.Node))
                        {
                            continue;
                        }

                        if (TypeOf(name) == "audio.Track")
                        {
                            subgraph.SetInput(Short(name), source);
                        }
                        else if (valueOf.TryGetValue(source, out var value) && controlInputs.TryGetValue(value, out var controlInput))
                        {
                            subgraph.SetInput(controlInput, source);
                        }
                        else if (!plug.StartsWith("audio", StringComparison.Ordinal))
                        {
                            Warnings.Add($"{name}/{plug} reads {source.Node} from outside the subgraph {prefix}");
                        }
                    }
                }

                var firstOutput = inner.Where(static name => TypeOf(name) == "audio.Output").Order(StringComparer.Ordinal).FirstOrDefault();

                if (graphName != null)
                {
                    foreach (var name in inner.Where(static name => TypeOf(name) == "audio.Output"))
                    {
                        if (nodes[name].Inputs.FirstOrDefault().Value is { Node: not null } source && IsInner(source.Node))
                        {
                            SubgraphOutputs.Add((graphName, new PlugSource(Short(source.Node), source.Plug), Short(name)));
                        }
                    }
                }

                foreach (var node in nodes.Values)
                {
                    if (inner.Contains(node.Name))
                    {
                        continue;
                    }

                    for (var k = 0; k < node.Inputs.Count; k++)
                    {
                        var (plug, source) = node.Inputs[k];

                        if (!IsInner(source.Node))
                        {
                            continue;
                        }

                        var output = TypeOf(source.Node) == "audio.Output" ? source.Node : firstOutput;

                        if (source.Plug.StartsWith("audio", StringComparison.Ordinal) && output != null)
                        {
                            node.Inputs[k] = new(plug, new(subgraphName, Short(output)));
                        }
                        else
                        {
                            node.Inputs[k] = new(plug, new(subgraphName, "control.Output1"));
                            Warnings.Add($"{node.Name}/{plug} reads {source.Node}/{source.Plug} of the subgraph {prefix}, wired to its first control output");
                        }
                    }
                }

                foreach (var (value, source) in valueSources.ToList())
                {
                    if (IsInner(source.Node))
                    {
                        valueSources[value] = new(subgraphName, "control.Output1");
                    }
                }

                foreach (var name in inner)
                {
                    nodes.Remove(name);
                }
            }
        }

        private void Finish()
        {
            CollapseSubgraphs();
            RenameOutputs();

            // Control meters "control.MeterN/value:Out" alias the value they meter
            foreach (var (value, outputs) in meteredOutputs)
            {
                foreach (var (name, _) in outputs)
                {
                    if (TypeOf(name) != "control.Meter" || name.Contains('/', StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var node = Node(name, "control.Meter");
                    node.MeteredValue = value;
                    node.Properties = KVObject.Collection();

                    if (Source(value) is { } source && source.Node != name)
                    {
                        node.SetInput("value", source);
                        node.Properties.Add("m_flValue", 0.0f);
                    }
                    else
                    {
                        node.Properties.Add("m_flValue", graph.Value(value));
                    }
                }
            }

            // Readers of a metered value read it through the meter
            var meters = nodes.Values.Where(static node => node.Type == "control.Meter" && node.MeteredValue >= 0).ToDictionary(static node => node.MeteredValue);
            var valueOf = new Dictionary<PlugSource, int>();

            foreach (var (value, source) in valueSources)
            {
                valueOf.TryAdd(source, value);
            }

            foreach (var node in nodes.Values)
            {
                if (node.Type == "control.Meter")
                {
                    continue;
                }

                for (var k = 0; k < node.Inputs.Count; k++)
                {
                    var (plug, source) = node.Inputs[k];

                    if (valueOf.TryGetValue(source, out var value) && meters.TryGetValue(value, out var meter) && source.Node != meter.Name)
                    {
                        node.Inputs[k] = new(plug, new(meter.Name, "value:Out"));
                    }
                }
            }

            // An audio output feeds one input, so outputs the compiled data reads more than once get a splitter
            var consumers = new Dictionary<PlugSource, List<(MixNode Node, string Plug)>>();

            foreach (var node in nodes.Values.ToList())
            {
                foreach (var (plug, source) in node.Inputs)
                {
                    if ((plug.StartsWith("audio", StringComparison.Ordinal) && source.Plug.StartsWith("audio", StringComparison.Ordinal)) || plug == "vsnd:ImpulseResponse")
                    {
                        (consumers.TryGetValue(source, out var list) ? list : consumers[source] = []).Add((node, plug));
                    }
                }
            }

            foreach (var (source, readers) in consumers)
            {
                if (readers.Count < 2 || source.Plug == "Vsnd Name")
                {
                    continue;
                }

                var splitter = Node(Fresh("audio.Splitter"), "audio.Splitter");
                splitter.FriendlyName = string.Concat(nodes.TryGetValue(source.Node, out var sourceNode) ? sourceNode.FriendlyName ?? source.Node : source.Node, "_split");

                for (var k = 1; k <= readers.Count; k++)
                {
                    splitter.Properties.Add(Indexed("m_flVolume", k), 1.0f);
                }

                splitter.SetInput("audio:In", source);

                for (var k = 0; k < readers.Count; k++)
                {
                    readers[k].Node.SetInput(readers[k].Plug, new(splitter.Name, k == 1 ? "audio.Out2" : Indexed("audio:Out", k + 1)));
                }

                Warnings.Add($"{source.Node}/{source.Plug} feeds {readers.Count} inputs, inserted {splitter.Name}");
            }

            foreach (var node in nodes.Values)
            {
                if (node.DetailedPlugs)
                {
                    ApplyDetailedPlugNames(node);
                }

                if (node.Processor != null)
                {
                    node.Properties = ProcessorProperties(node);
                }

                node.FriendlyName ??= node.Name;
            }

            if (graph.Format == MixGraphFormat.Heap)
            {
                RecoverGenerationIds();
            }
        }

        private const int MaxGenerationId = 4096;

        /// <summary>
        /// Heap graphs give processors and meters a debug id, the token of the node's name, generation and class.
        /// The generation counts the edits of a node, so it is recovered from whichever debug id the node has.
        /// </summary>
        private void RecoverGenerationIds()
        {
            // Inlined subgraphs meter their nodes by name too, so a name can have several ids
            var meterIds = new List<(string Name, uint Id)>();

            for (var k = 0; k < graph.AudioMeters.Count; k++)
            {
                if (graph.AudioMeterDebugIds[k] != 0)
                {
                    meterIds.Add((graph.AudioMeters[k], graph.AudioMeterDebugIds[k]));
                }
            }

            foreach (var node in nodes.Values)
            {
                if (!NodeTypesByName.TryGetValue(node.Type, out var type) || type.ClassName.Length == 0)
                {
                    continue;
                }

                // Meters of a plug ("audio.Splitter1/audio:Out1") hash the plug after the class
                var candidates = new List<(uint Id, string Suffix)>();

                if (node.Processor?.DebugId is { } processorId and not 0)
                {
                    candidates.Add((processorId, string.Empty));
                }

                candidates.AddRange(meterIds.Where(meter => meter.Name == node.FriendlyName).Select(static meter => (meter.Id, string.Empty)));
                candidates.AddRange(meterIds.Where(meter => meter.Name.StartsWith(node.Name + "/", StringComparison.Ordinal))
                    .Select(meter => (meter.Id, meter.Name[node.Name.Length..])));

                var found = false;

                foreach (var (id, suffix) in candidates)
                {
                    for (var generation = 0; generation <= MaxGenerationId && !found; generation++)
                    {
                        if (StringToken.Get(string.Create(CultureInfo.InvariantCulture, $"{node.FriendlyName}{generation}{type.ClassName}{suffix}")) == id)
                        {
                            found = true;

                            if (generation != 0)
                            {
                                node.Properties["m_nGenerationId"] = generation;
                            }
                        }
                    }

                    if (found)
                    {
                        break;
                    }
                }
            }
        }

        private float? ConstantControl(MixNode node, string plug)
            => node.ControlValues.TryGetValue(plug, out var value) && !node.HasInput(plug) ? graph.Value(value) : null;

        private KVObject ProcessorProperties(MixNode node)
        {
            var processor = node.Processor!;
            var desc = processor.Desc;
            var properties = KVObject.Collection();

            void Copy(string to, string from)
            {
                if (desc.TryGetValue(from, out var value) && !value.IsNull)
                {
                    properties.Add(to, value);
                }
            }

            void CopyFilter(KVObject filter, string type, string frequency, string q, string gain)
            {
                if (filter.TryGetValue("m_nFilterType", out var filterType))
                {
                    properties.Add(type, filterType);
                }

                properties.Add(frequency, filter.GetFloatProperty("m_flCutoffFreq"));
                properties.Add(q, filter.GetFloatProperty("m_flQ"));
                properties.Add(gain, filter.GetFloatProperty("m_fldbGain"));
            }

            switch (node.Type)
            {
                case "audio.Filter":
                    properties.Add("m_filterType", desc.GetStringProperty("m_nFilterType", "FILTER_LOWPASS"));
                    properties.Add("m_nChannels", processor.Channels);
                    properties.Add("m_flFrequency", desc.GetFloatProperty("m_flCutoffFreq"));
                    properties.Add("m_flQ", desc.GetFloatProperty("m_flQ"));
                    properties.Add("m_fldbGain", desc.GetFloatProperty("m_fldbGain"));
                    Copy("m_nFilterSlope", "m_nFilterSlope");
                    break;

                case "audio.Delay":
                    properties.Add("m_nChannels", processor.Channels);
                    Copy("m_flDelay", "m_flDelay");
                    Copy("m_fldbDirectGain", "m_flDirectGain");
                    Copy("m_fldbDelayGain", "m_flDelayGain");
                    Copy("m_fldbFeedbackGain", "m_flFeedbackGain");
                    Copy("m_flWidth", "m_flWidth");
                    Copy("m_bEnableFilter", "m_bEnableFilter");

                    if (desc.GetSubCollection("m_feedbackFilter") is { } feedbackFilter)
                    {
                        CopyFilter(feedbackFilter, "m_filterType", "m_flFrequency", "m_flQ", "m_fldbGain");
                    }

                    break;

                case "audio.ModDelay":
                case "audio.Flanger":
                    Copy("m_bPhaseInvert", "m_bPhaseInvert");
                    Copy("m_flGlideTime", "m_flGlideTime");
                    Copy("m_flDelay", "m_flDelay");
                    Copy("m_flFeedback", "m_flFeedbackGain");
                    Copy("m_flFeedfoward", "m_flFeedforwardGain");
                    Copy("m_flGain", "m_flOutputGain");
                    Copy("m_flModRate", "m_flModRate");
                    Copy("m_flModDepth", "m_flModDepth");

                    if (desc.GetSubCollection("m_feedbackFilter") is { } modFilter)
                    {
                        CopyFilter(modFilter, "m_filterType", "m_flFrequency", "m_flQ", "m_flFilterGain");
                    }

                    Copy("m_bAntialiasing", "m_bApplyAntialiasing");
                    break;

                case "audio.Dynamics":
                    properties.Add("m_nChannels", processor.Channels);
                    Copy("m_fldbNoiseGateThreshold", "m_fldbNoiseGateThreshold");
                    Copy("m_fldbGain", "m_fldbGain");
                    Copy("m_fldbCompressionThreshold", "m_fldbCompressionThreshold");
                    Copy("m_fldbLimiterThreshold", "m_fldbLimiterThreshold");
                    Copy("m_fldbKneeWidth", "m_fldbKneeWidth");
                    Copy("m_flRatio", "m_flRatio");
                    Copy("m_flLimiterRatio", "m_flLimiterRatio");
                    Copy("m_flAttackTime", "m_flAttackTimeMS");
                    Copy("m_flReleaseTime", "m_flReleaseTimeMS");
                    Copy("m_flRMSTime", "m_flRMSTimeMS");
                    Copy("m_flWetMix", "m_flWetMix");
                    Copy("m_bPeakMode", "m_bPeakMode");
                    break;

                case "audio.Compressor":
                case "audio.Limiter":
                    properties.Add("m_nChannels", processor.Channels);
                    properties.Add("m_desc", desc);
                    properties.Add("m_bIsLimiter", node.Type == "audio.Limiter");
                    break;

                case "audio.DualCompressor":
                    properties.Add("m_nChannels", processor.Channels);
                    properties.Add("m_desc", desc);
                    break;

                case "audio.Dynamics3Band":
                    properties.Add("m_nChannels", processor.Channels);
                    Copy("m_fldbOutputGain", "m_fldbGainOutput");
                    Copy("m_flRMSTime", "m_flRMSTimeMS");
                    Copy("m_flDepth", "m_flDepth");
                    Copy("m_flWetMix", "m_flWetMix");
                    Copy("m_flTimeScale", "m_flTimeScale");
                    Copy("m_fldbKneeWidth", "m_fldbKneeWidth");
                    Copy("m_flLowCutoffFreq", "m_flLowCutoffFreq");
                    Copy("m_flHighCutoffFreq", "m_flHighCutoffFreq");
                    Copy("m_bPeakMode", "m_bPeakMode");
                    Copy("m_bands", "m_bandDesc");
                    break;

                case "audio.EQ8":
                {
                    properties.Add("m_nChannels", processor.Channels);
                    var stages = KVObject.Array();

                    foreach (var stage in desc.GetArray("m_stages") ?? [])
                    {
                        var editorStage = KVObject.Collection();
                        editorStage.Add("m_filterType", stage.GetStringProperty("m_nFilterType", "FILTER_PEAKING_EQ"));
                        editorStage.Add("m_flFrequency", stage.GetFloatProperty("m_flCutoffFreq"));
                        editorStage.Add("m_flQ", stage.GetFloatProperty("m_flQ"));
                        editorStage.Add("m_fldbGain", stage.GetFloatProperty("m_fldbGain"));

                        if (stage.TryGetValue("m_nFilterSlope", out var slope))
                        {
                            editorStage.Add("m_nFilterSlope", slope);
                        }

                        if (stage.TryGetValue("m_nChannelSet", out var channelSet))
                        {
                            editorStage.Add("m_nChannelSet", channelSet);
                        }

                        editorStage.Add("m_bEnable", stage.GetBooleanProperty("m_bEnabled"));
                        stages.Add(editorStage);
                    }

                    properties.Add("m_stages", stages);
                    break;
                }

                case "audio.Shaper":
                case "audio.Utility":
                case "audio.Osc":
                case "audio.AutoFilter":
                case "audio.Convolution":
                    properties.Add("m_desc", desc);
                    break;

                case "audio.PitchShift":
                    properties.Add("m_nChannels", processor.Channels);
                    properties.Add("m_flPitchScale", ConstantControl(node, "pitchScale") ?? 1.0f);

                    // The compiler keeps the grain length in samples at 44.1 kHz
                    if (desc.TryGetValue("m_nGrainSampleCount", out var grainSamples) && !grainSamples.IsNull)
                    {
                        properties.Add("m_flGrainMs", (long)grainSamples * 1000f / 44100f);
                    }

                    Copy("m_nProcType", "m_nProcType");
                    Copy("m_nQuality", "m_nQuality");
                    break;

                case "audio.Envelope":
                    Copy("m_flAttackTime", "m_flAttackTimeMS");
                    Copy("m_flHoldTime", "m_flHoldTimeMS");
                    Copy("m_flReleaseTime", "m_flReleaseTimeMS");
                    break;

                case "audio.Processor":
                case "audio.EffectChain":
                    properties.Add("m_nChannels", processor.Channels);
                    properties.Add("m_effectName", processor.EffectName);
                    properties.Add("m_flXFade", processor.CrossFade);
                    break;

                case "sys.SubgraphSwitch":
                {
                    var subgraphs = processor.Subgraphs;
                    properties.Add("bUseDetailedPlugNames", node.DetailedPlugs);
                    properties.Add("defaultSubgraph", SelectableSubgraph(subgraphs.Length > 0 ? subgraphs[^1] : string.Empty));
                    Copy("interpolationMode", "m_interpolationMode");
                    Copy("bOnlyTailsOnFadeOut", "m_bOnlyTailsOnFadeOut");
                    Copy("flTransitionTime", "m_flInterpolationTime");
                    properties.Add("nChannels", processor.Channels);
                    properties.Add("subgraphs", KVObject.Array(subgraphs.SkipLast(1).Select(SelectableSubgraph)));
                    break;
                }

                case "audio.SteamAudioSource":
                {
                    var interpolation = ConstantControl(node, "interpolation") ?? 0.0f;
                    properties.Add("m_nInterpolation", interpolation != 0 ? "SA_HRTFINTEROP_BILINEAR" : "SA_HRTFINTEROP_NEAREST");
                    properties.Add("m_flDirectMixLevel", ConstantControl(node, "directmixlevel") ?? 1.0f);
                    properties.Add("m_bEnablePerspectiveCorrection", (ConstantControl(node, "perspectivecorrection") ?? 0.0f) != 0);
                    properties.Add("m_bRelativePosition", (ConstantControl(node, "relativeposition") ?? 0.0f) != 0);
                    break;
                }

                case "audio.SteamAudioPathing":
                    properties.Add("m_flPathingMixLevel", ConstantControl(node, "pathingmixlevel") ?? 1.0f);
                    break;

                default:
                    // The other processors keep their compiled field names in the editor
                    foreach (var (key, value) in desc)
                    {
                        properties.Add(key, value);
                    }

                    break;
            }

            return properties;
        }

        private KVObject SelectableSubgraph(string name)
        {
            var subgraph = KVObject.Collection();

            if (graph.Format == MixGraphFormat.Heap)
            {
                subgraph.Add("_class", "CSelectableSubgraph");
            }

            subgraph.Add("file", SubgraphFile(name));
            subgraph.Add("subgraphName", name);
            return subgraph;
        }

        public Func<string, string> SubgraphFile { get; init; } = static _ => string.Empty;

        public void AddControlStoreFallbacks()
        {
            // Control inputs nothing reads were wired to plugs the compiler ignores; wire them to the next
            // compiled node so that the input still compiles
            var used = nodes.Values.SelectMany(static node => node.Inputs).Select(static input => input.Value).ToHashSet();

            foreach (var (command, _, name) in controlStores)
            {
                if (used.Contains(new PlugSource(name, "value")))
                {
                    continue;
                }

                var next = blocks.Where(block => block.Start > command)
                    .SelectMany(block => block.Created)
                    .Select(created => nodes[created])
                    .FirstOrDefault(static created => created.Type is not ("control.Input" or "control.Automatic"))
                    ?? nodes.Values.FirstOrDefault(static node => node.Type == "audio.Output");

                if (next == null)
                {
                    continue;
                }

                var plug = new string([.. (nodes[name].FriendlyName ?? name).Select(static c => char.IsLetterOrDigit(c) ? c : '_')]);
                next.SetInput(plug, new(name, "value"));
                Warnings.Add($"{name} ({nodes[name].FriendlyName}) has no reader, wired to {next.Name}/{plug}");
            }
        }
    }
}
