using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

public sealed partial class VMixGraphExtract
{
    /// <summary>
    /// The generations of compiled mix graphs, which differ in how commands, processors and values are stored.
    /// </summary>
    internal enum MixGraphFormat
    {
        /// <summary>Graphs compiled by vmixtool itself and copied into the source (Half-Life: Alyx, Aperture Desk Job).</summary>
        EditorCompiled,

        /// <summary>Processors with a type and parameter block, values in a float table (2024 and 2025).</summary>
        ProcessorTypes,

        /// <summary>Processors as described classes, values in a float table (2026 until the per-file graphs).</summary>
        ProcessorClasses,

        /// <summary>Values and inputs addressed as offsets into a heap, one graph per file.</summary>
        Heap,
    }

    internal enum MixOp
    {
        Unknown,
        SubmixDebug,
        SubmixMeter,
        SubmixMeterSpectrum,
        SubmixProcess,
        SubmixGenerate,
        SubmixGenerateSidechain,
        SubmixCopy,
        SubmixAccumulate,
        SubmixOutput,
        SubmixOutputX2,
        ControlCopy,
        ControlCondCopyIfNegative,
        ControlEvaluateCurve,
        ControlRemapLinear,
        ControlRemapLogLinear,
        ControlRemapSine,
        ControlSineBlend,
        ControlInputStore,
        ControlInputStoreDecibels,
        ControlOutputStore,
        ControlMax,
        ControlTransientInputStore,
        ControlTransientInputReset,
        ControlResetTimer,
        ControlIncrementTimer,
        ControlEvalEnvelope,
        ControlConvertDecibelsToGain,
        ProcessorSetControlValue,
        ProcessorSetControlArrayValue,
        ProcessorSetVsndValue,
        ProcessorSetNameInput,
        ProcessorStoreControlValue,
        ProcessorSetImpulseResponseValue,
        ImpulseResponseInputStore,
        ImpulseResponseReset,
        ImpulseResponseDelay,
        RemapVsndToImpulseResponse,
        BlendVsndsToImpulseResponse,
    }

    private static readonly Dictionary<string, MixOp> MixOpNames = new(StringComparer.Ordinal)
    {
        ["submix.debug"] = MixOp.SubmixDebug,
        ["submix.meter"] = MixOp.SubmixMeter,
        ["submix.spectrum"] = MixOp.SubmixMeterSpectrum,
        ["submix.process"] = MixOp.SubmixProcess,
        ["submix.generate"] = MixOp.SubmixGenerate,
        ["submix.generate.sidechain"] = MixOp.SubmixGenerateSidechain,
        ["submix.copy"] = MixOp.SubmixCopy,
        ["submix.accumulate_single"] = MixOp.SubmixAccumulate,
        ["submix.output"] = MixOp.SubmixOutput,
        ["submix.outputx2"] = MixOp.SubmixOutputX2,
        ["control.copy"] = MixOp.ControlCopy,
        ["control.curve"] = MixOp.ControlEvaluateCurve,
        ["control.remap_linear"] = MixOp.ControlRemapLinear,
        ["control.remap_loglinear"] = MixOp.ControlRemapLogLinear,
        ["control.remap_sine"] = MixOp.ControlRemapSine,
        ["control.sine_blend"] = MixOp.ControlSineBlend,
        ["control.store"] = MixOp.ControlInputStore,
        ["control.store_db"] = MixOp.ControlInputStoreDecibels,
        ["control.output"] = MixOp.ControlOutputStore,
        ["control.max"] = MixOp.ControlMax,
        ["processor.setparameter"] = MixOp.ProcessorSetControlValue,
        ["processor.setparameterarray"] = MixOp.ProcessorSetControlArrayValue,
        ["processor.setvsnd"] = MixOp.ProcessorSetVsndValue,
        ["processor.setnameinput"] = MixOp.ProcessorSetNameInput,
        ["processor.storeparameter"] = MixOp.ProcessorStoreControlValue,
        ["processor.setimpulseresponse"] = MixOp.ProcessorSetImpulseResponseValue,
        ["CMD_SUBMIX_DEBUG"] = MixOp.SubmixDebug,
        ["CMD_SUBMIX_METER"] = MixOp.SubmixMeter,
        ["CMD_SUBMIX_METER_SPECTRUM"] = MixOp.SubmixMeterSpectrum,
        ["CMD_SUBMIX_PROCESS"] = MixOp.SubmixProcess,
        ["CMD_SUBMIX_GENERATE"] = MixOp.SubmixGenerate,
        ["CMD_SUBMIX_GENERATE_SIDECHAIN"] = MixOp.SubmixGenerateSidechain,
        ["CMD_SUBMIX_COPY"] = MixOp.SubmixCopy,
        ["CMD_SUBMIX_ACCUMULATE"] = MixOp.SubmixAccumulate,
        ["CMD_SUBMIX_OUTPUT"] = MixOp.SubmixOutput,
        ["CMD_SUBMIX_OUTPUTx2"] = MixOp.SubmixOutputX2,
        ["CMD_CONTROL_COPY"] = MixOp.ControlCopy,
        ["CMD_CONTROL_COND_COPY_IF_NEGATIVE"] = MixOp.ControlCondCopyIfNegative,
        ["CMD_CONTROL_EVALUATE_CURVE"] = MixOp.ControlEvaluateCurve,
        ["CMD_CONTROL_REMAP_LINEAR"] = MixOp.ControlRemapLinear,
        ["CMD_CONTROL_REMAP_LOGLINEAR"] = MixOp.ControlRemapLogLinear,
        ["CMD_CONTROL_REMAP_SINE"] = MixOp.ControlRemapSine,
        ["CMD_CONTROL_SINE_BLEND"] = MixOp.ControlSineBlend,
        ["CMD_CONTROL_INPUT_STORE"] = MixOp.ControlInputStore,
        ["CMD_CONTROL_OUTPUT_STORE"] = MixOp.ControlOutputStore,
        ["CMD_CONTROL_MAX"] = MixOp.ControlMax,
        ["CMD_CONTROL_TRANSIENT_INPUT_STORE"] = MixOp.ControlTransientInputStore,
        ["CMD_CONTROL_TRANSIENT_INPUT_RESET"] = MixOp.ControlTransientInputReset,
        ["CMD_CONTROL_RESET_TIMER"] = MixOp.ControlResetTimer,
        ["CMD_CONTROL_INCREMENT_TIMER"] = MixOp.ControlIncrementTimer,
        ["CMD_CONTROL_EVAL_ENVELOPE"] = MixOp.ControlEvalEnvelope,
        ["CMD_CONTROL_CONVERT_DB_TO_GAIN"] = MixOp.ControlConvertDecibelsToGain,
        ["CMD_PROCESSOR_SET_CONTROL_VALUE"] = MixOp.ProcessorSetControlValue,
        ["CMD_PROCESSOR_SET_CONTROL_ARRAYVALUE"] = MixOp.ProcessorSetControlArrayValue,
        ["CMD_PROCESSOR_SET_VSND_VALUE"] = MixOp.ProcessorSetVsndValue,
        ["CMD_PROCESSOR_SET_NAME_INPUT"] = MixOp.ProcessorSetNameInput,
        ["CMD_PROCESSOR_STORE_CONTROL_VALUE"] = MixOp.ProcessorStoreControlValue,
        ["CMD_PROCESSOR_SET_IMPULSERESPONSE_VALUE"] = MixOp.ProcessorSetImpulseResponseValue,
        ["CMD_IMPULSERESPONSE_INPUT_STORE"] = MixOp.ImpulseResponseInputStore,
        ["CMD_IMPULSERESPONSE_RESET"] = MixOp.ImpulseResponseReset,
        ["CMD_IMPULSERESPONSE_DELAY"] = MixOp.ImpulseResponseDelay,
        ["CMD_REMAP_VSND_TO_IMPULSERESPONSE"] = MixOp.RemapVsndToImpulseResponse,
        ["CMD_BLEND_VSNDS_TO_IMPULSERESPONSE"] = MixOp.BlendVsndsToImpulseResponse,
    };

    /// <summary>
    /// One compiled command. Operands are indices into the submix, value, meter or input tables, -1 when unused.
    /// </summary>
    internal readonly record struct MixCommand(MixOp Op, string Name, uint ParamName, int OutputSubmix, int InputSubmix0, int InputSubmix1, int Processor, int Value0, int Value1)
    {
        /// <summary>The operands as stored in heap graphs, where some commands keep constants in them instead of offsets.</summary>
        public (uint InputSubmix0, uint InputSubmix1, uint Value1) Raw { get; init; }
    }

    private static readonly string[] OffsetCategories = ["NULL_POINTER", "HEAP_OFFSET", "INPUT_INDEX", "SUBMIX_INDEX"];

    private static readonly string[] OffsetTypes =
    [
        "VO_CHAR", "VO_ARRAY", "VO_BOOL", "VO_FLOAT", "VO_UINT32", "VO_INT32", "VO_VECTOR", "VO_QUATERNION", "VO_CUBIC_SPLINE",
        "VO_VSND_INPUT", "VO_FLOAT_UTLVECTOR", "VO_SHAREDPTR_IR",
    ];

    /// <summary>
    /// The 32 bits an offset is packed into: the category in the top 3 bits, then 10 bits of type, then a 19 bit index.
    /// </summary>
    private static uint RawOperand(KVObject obj, params string[] keys)
    {
        var value = keys.Select(key => obj.TryGetValue(key, out var found) ? found : null).FirstOrDefault(static found => found != null);
        if (value == null)
        {
            return 0;
        }

        // Graphs before the heap keep plain integers, which commands with constants fill with float bits
        if (value.ValueType != KVValueType.Collection)
        {
            return value.ValueType is KVValueType.String or KVValueType.Boolean ? 0 : unchecked((uint)(long)value);
        }

        static uint Field(KVObject value, string key, string[] names)
        {
            if (!value.TryGetValue(key, out var field))
            {
                return 0;
            }

            return field.ValueType == KVValueType.String ? (uint)Math.Max(0, Array.IndexOf(names, (string)field)) : unchecked((uint)(long)field);
        }

        return (Field(value, "category", OffsetCategories) << 29) | (Field(value, "type", OffsetTypes) << 19) | (unchecked((uint)value.GetIntegerProperty("index")) & 0x7FFFF);
    }

    internal sealed class MixProcessor
    {
        public required string Name { get; init; }
        public required string NodeType { get; init; }
        public int Channels { get; init; } = -1;
        public float CrossFade { get; init; } = 0.1f;
        public KVObject Desc { get; init; } = KVObject.Collection();
        public string EffectName { get; init; } = string.Empty;
        public string[] Subgraphs { get; init; } = [];
        public uint DebugId { get; init; }

        /// <summary>Parameters bound to values, by their key without the "m_param" prefix (heap graphs only).</summary>
        public Dictionary<string, int> Parameters { get; } = new(StringComparer.Ordinal);

        /// <summary>Parameters bound to graph inputs, by their key without the "m_param" prefix (heap graphs only).</summary>
        public Dictionary<string, int> InputParameters { get; } = new(StringComparer.Ordinal);
    }

    internal sealed record MixSubmix(string Name, uint SoloNameHash, int Channels, string SendOperator, string MixDownRule, string[] Sends)
    {
        public string UpMixRule { get; init; } = string.Empty;
    }

    internal sealed record MixNamedValue(string Name, int Value);

    internal sealed record MixControlInput(string Name, float DefaultValue, int Value);

    internal sealed record MixStringInput(string Name, string DefaultValue, int Processor);

    /// <summary>
    /// A compiled mix graph of any generation, normalized so that one decompiler handles them all.
    /// </summary>
    internal sealed class MixGraph
    {
        public required KVObject Source { get; init; }
        public required MixGraphFormat Format { get; init; }
        public string Name { get; init; } = string.Empty;
        public int OutputChannels { get; init; } = -1;
        public bool IsMainGraph { get; init; }
        public List<MixCommand> Commands { get; } = [];
        public float[] Values { get; set; } = [];

        /// <summary>The heap as stored, for the counts and offsets that are kept among its values.</summary>
        public uint[] RawValues { get; set; } = [];
        public List<MixProcessor> Processors { get; } = [];
        public List<MixSubmix> Submixes { get; } = [];
        public List<string> AudioMeters { get; } = [];
        public List<uint> AudioMeterDebugIds { get; } = [];
        public List<MixNamedValue> ControlMeters { get; } = [];
        public List<MixNamedValue> NameInputMeters { get; } = [];
        public List<MixControlInput> ControlInputs { get; } = [];
        public List<MixStringInput> NameInputs { get; } = [];
        public List<MixStringInput> VsndInputs { get; } = [];
        public List<MixNamedValue> ControlInputArrays { get; } = [];
        public List<float[]> ValueArrays { get; } = [];
        public float[] ControlPoints { get; set; } = [];
        public List<(int Start, int Count)> Curves { get; } = [];
        public List<MixNamedValue> AutomaticControlInputs { get; } = [];
        public List<MixControlInput> ControlOutputs { get; } = [];
        public List<MixControlInput> TransientInputs { get; } = [];
        public List<string> AdditionalOutputs { get; } = [];

        /// <summary>The graph inputs of heap graphs, which the input tables above index.</summary>
        public List<string> GraphInputs { get; } = [];

        /// <summary>The graph input of each control input array, in heap graphs.</summary>
        public List<int> ArrayInputs { get; } = [];

        public float Value(int index) => index >= 0 && index < Values.Length ? Values[index] : 0f;
    }

    private static readonly string[] SubmixNameKeys = ["m_name", "name"];

    private static string GetString(KVObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetValue(key, out var value) && value.ValueType == KVValueType.String)
            {
                return (string)value;
            }
        }

        return string.Empty;
    }

    private static bool TryGet(KVObject obj, out KVObject value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetValue(key, out value!) && !value.IsNull)
            {
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static long GetInteger(KVObject obj, long defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.ContainsKey(key))
            {
                return obj.GetIntegerProperty(key, defaultValue);
            }
        }

        return defaultValue;
    }

    private static float GetFloat(KVObject obj, float defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.ContainsKey(key))
            {
                return obj.GetFloatProperty(key, defaultValue);
            }
        }

        return defaultValue;
    }

    private static IReadOnlyList<KVObject> GetList(KVObject obj, string key)
        => obj.TryGetValue(key, out var value) && value.IsArray ? value.Elements : [];

    /// <summary>
    /// An operand: a plain index in older graphs, or a {category, type, index} offset in heap graphs.
    /// </summary>
    private static int Operand(KVObject obj, params string[] keys)
    {
        if (!TryGet(obj, out var value, keys))
        {
            return -1;
        }

        if (value.ValueType == KVValueType.Collection)
        {
            return GetString(value, "category") == "NULL_POINTER" ? -1 : (int)value.GetIntegerProperty("index");
        }

        return (int)(long)value;
    }

    private static uint UnsignedProperty(KVObject obj, string key)
        => obj.TryGetValue(key, out var value) && !value.IsNull ? unchecked((uint)(long)value) : 0;

    internal static MixGraphFormat DetectFormat(KVObject root)
    {
        var graphs = GetList(root, "Graphs");

        if (graphs.Count == 0)
        {
            return root.ContainsKey("m_graphDesc") ? MixGraphFormat.Heap : MixGraphFormat.ProcessorTypes;
        }

        if (graphs.Any(static graph => graph.ContainsKey("m_heap")))
        {
            return MixGraphFormat.Heap;
        }

        if (graphs.Any(static graph => GetList(graph, "m_Processors").Any(static processor => processor.ContainsKey("_class"))))
        {
            return MixGraphFormat.ProcessorClasses;
        }

        // vmixtool wrote processors with a globals flag and graphs without name input meters
        if (root.ContainsKey("dsp_preview")
            || graphs.Any(static graph => GetList(graph, "m_Processors").Any(static processor => processor.ContainsKey("m_bAffectsGlobals"))))
        {
            return MixGraphFormat.EditorCompiled;
        }

        return graphs.Any(static graph => graph.ContainsKey("m_nameInputMeters")) || !graphs.Any(static graph => graph.ContainsKey("m_controlOutputs"))
            ? MixGraphFormat.ProcessorTypes
            : MixGraphFormat.EditorCompiled;
    }

    internal static MixGraph ReadGraph(KVObject graph, MixGraphFormat format)
    {
        var result = new MixGraph
        {
            Source = graph,
            Format = format,
            Name = GetString(graph, "Name", "m_name"),
            OutputChannels = (int)GetInteger(graph, -1, "m_nGraphOutputChannels"),
            IsMainGraph = graph.GetBooleanProperty("m_bIsMainGraph"),
        };

        foreach (var command in GetList(graph, "m_MixCommands"))
        {
            var name = GetString(command, "command");
            result.Commands.Add(new MixCommand(
                MixOpNames.GetValueOrDefault(name, MixOp.Unknown),
                name,
                UnsignedProperty(command, "paramName"),
                Operand(command, "outputSubmix"),
                Operand(command, "inputSubmix0"),
                Operand(command, "inputSubmix1"),
                (int)command.GetIntegerProperty("processor", -1),
                Operand(command, "inputValue0", "m_nInputValue0"),
                Operand(command, "inputValue1", "m_nInputValue1"))
            {
                Raw = (RawOperand(command, "inputSubmix0"), RawOperand(command, "inputSubmix1"), RawOperand(command, "inputValue1", "m_nInputValue1")),
            });
        }

        if (format == MixGraphFormat.Heap)
        {
            var storage = graph.GetSubCollection("m_heap") is { } heap ? GetList(heap, "m_storage") : [];
            result.RawValues = [.. storage.Select(static value => unchecked((uint)(long)value))];
            result.Values = [.. result.RawValues.Select(static value => BitConverter.UInt32BitsToSingle(value))];
        }
        else
        {
            result.Values = [.. GetList(graph, "m_values").Select(static value => (float)(double)value)];
        }

        foreach (var processor in GetList(graph, "m_Processors"))
        {
            result.Processors.Add(ReadProcessor(processor));
        }

        foreach (var submix in GetList(graph, "m_Submixes"))
        {
            string[] sends = TryGet(submix, out var sendNames, "m_SendNames")
                ? [.. sendNames.Elements.Select(static send => (string)send)]
                : [.. Enumerable.Range(0, 4).Select(k => GetString(submix, $"send{k}"))];

            var mixDown = submix.TryGetValue("m_nMixDownRule", out var rule) || submix.TryGetValue("mixDownRule", out rule)
                ? rule.ValueType == KVValueType.String ? (string)rule : ((long)rule).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;

            result.Submixes.Add(new MixSubmix(
                GetString(submix, SubmixNameKeys),
                UnsignedProperty(submix, submix.ContainsKey("m_nSoloNameHash") ? "m_nSoloNameHash" : "soloname"),
                (int)GetInteger(submix, -1, "m_nChannels", "channels"),
                GetString(submix, "m_nSendOperator", "sendOperator"),
                mixDown,
                sends)
            {
                UpMixRule = GetString(submix, "m_nUpMixRule"),
            });
        }

        foreach (var meter in GetList(graph, "m_audioMeters"))
        {
            result.AudioMeters.Add(GetString(meter, "m_name", "name"));
            result.AudioMeterDebugIds.Add(UnsignedProperty(meter, "m_nDebugId"));
        }

        foreach (var meter in GetList(graph, "m_controlMeters"))
        {
            result.ControlMeters.Add(new MixNamedValue(GetString(meter, "m_name", "name"), Operand(meter, "m_nValueIndex", "value")));
        }

        foreach (var meter in GetList(graph, "m_nameInputMeters"))
        {
            result.NameInputMeters.Add(new MixNamedValue(GetString(meter, "m_name", "name"), Operand(meter, "m_nValueIndex", "value")));
        }

        foreach (var input in GetList(graph, "m_controlInputs"))
        {
            result.ControlInputs.Add(new MixControlInput(GetString(input, "m_name", "name"), GetFloat(input, 0f, "m_flDefaultValue", "value"), -1));
        }

        foreach (var input in GetList(graph, "m_nameInputs"))
        {
            result.NameInputs.Add(new MixStringInput(GetString(input, "m_name", "name"), GetString(input, "m_defaultValue", "value"), (int)GetInteger(input, -1, "m_nProcessor", "processor")));
        }

        foreach (var input in GetList(graph, "m_vsndInputs"))
        {
            result.VsndInputs.Add(new MixStringInput(GetString(input, "m_name", "name"), GetString(input, "m_defaultValue", "value"), (int)GetInteger(input, -1, "m_nProcessor", "processor")));
        }

        foreach (var array in GetList(graph, "m_controlInputArrays"))
        {
            result.ControlInputArrays.Add(new MixNamedValue(GetString(array, "m_name", "name"), (int)GetInteger(array, 0, "m_nArrayIndex", "index")));
        }

        foreach (var array in GetList(graph, "m_valueArrays"))
        {
            result.ValueArrays.Add([.. array.Elements.Select(static value => (float)(double)value)]);
        }

        result.ControlPoints = [.. GetList(graph, "m_controlPoints").Select(static value => (float)(double)value)];

        foreach (var curve in GetList(graph, "m_curves"))
        {
            // Newer graphs count the start in points of four floats, older ones in floats
            var start = curve.ContainsKey("m_nControlPointStart") ? 4 * (int)curve.GetIntegerProperty("m_nControlPointStart") : (int)GetInteger(curve, 0, "start");
            result.Curves.Add((start, (int)GetInteger(curve, 0, "m_nControlPointCount", "count")));
        }

        foreach (var input in GetList(graph, "m_automaticControlInputs"))
        {
            result.AutomaticControlInputs.Add(new MixNamedValue(GetString(input, "m_name", "name"), (int)GetInteger(input, -1, "m_nGraphInputIndex", "m_nControlInputIndex")));
        }

        foreach (var output in GetList(graph, "m_controlOutputs"))
        {
            result.ControlOutputs.Add(new MixControlInput(GetString(output, "m_name", "name"), GetFloat(output, 0f, "m_flDefaultValue", "value"), -1));
        }

        foreach (var input in GetList(graph, "m_controlTransientInputs"))
        {
            result.TransientInputs.Add(new MixControlInput(GetString(input, "m_name", "name"), GetFloat(input, 0f, "m_flDefaultValue", "value"), -1));
        }

        foreach (var output in GetList(graph, "m_additionalOutputs"))
        {
            result.AdditionalOutputs.Add(GetString(output, "m_name", "name"));
        }

        if (format == MixGraphFormat.Heap)
        {
            ReadGraphInputs(graph, result);
        }

        return result;
    }

    /// <summary>
    /// Heap graphs keep every input in one table: control inputs point at their value on the heap,
    /// name inputs at a string hash on it, and vsnd inputs nowhere, with their default kept beside them.
    /// </summary>
    private static void ReadGraphInputs(KVObject graph, MixGraph result)
    {
        var defaults = GetList(graph, "m_inputDefaultValues");
        var inputs = GetList(graph, "m_graphInputs");

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var name = GetString(input, "m_name");
            result.GraphInputs.Add(name);

            var defaultValue = i < defaults.Count && !defaults[i].IsNull ? GetString(defaults[i], "m_defaultValue") : string.Empty;

            if (!TryGet(input, out var offset, "m_nOffset") || GetString(offset, "category") == "NULL_POINTER")
            {
                // Arrays keep their default values beside them as well
                if (GetString(offset, "type") == "VO_FLOAT_UTLVECTOR")
                {
                    var values = i < defaults.Count && defaults[i].IsArray ? defaults[i].Elements.Select(static value => (float)(double)value).ToArray() : [];
                    result.ControlInputArrays.Add(new MixNamedValue(name, result.ValueArrays.Count));
                    result.ArrayInputs.Add(i);
                    result.ValueArrays.Add(values);
                    continue;
                }

                result.VsndInputs.Add(new MixStringInput(name, defaultValue, i));
                continue;
            }

            var index = (int)offset.GetIntegerProperty("index");
            switch (GetString(offset, "type"))
            {
                case "VO_FLOAT":
                    result.ControlInputs.Add(new MixControlInput(name, result.Value(index), index));
                    break;
                case "VO_UINT32" when name != "debug.solo":
                    // The default name is only kept as its token, usually the name of a subgraph the input selects
                    if (defaultValue.Length == 0 && index < result.RawValues.Length && result.RawValues[index] != 0)
                    {
                        var token = result.RawValues[index];
                        defaultValue = result.Processors.SelectMany(static processor => processor.Subgraphs).Append("core_null").Append("core.null")
                            .FirstOrDefault(subgraph => StringToken.Get(subgraph) == token) ?? string.Empty;
                    }

                    result.NameInputs.Add(new MixStringInput(name, defaultValue, index));
                    break;
            }
        }
    }

    private static MixProcessor ReadProcessor(KVObject processor)
    {
        var className = GetString(processor, "_class");
        var key = className.Length > 0
            ? className.Replace("CVMix", string.Empty, StringComparison.Ordinal).Replace("ProcessorDesc", string.Empty, StringComparison.Ordinal)
            : GetString(processor, "m_nProcessorType").Replace("VPROCESSOR_", string.Empty, StringComparison.Ordinal);

        var desc = TryGet(processor, out var descValue, "m_desc", "m_params") && descValue.ValueType == KVValueType.Collection
            ? descValue
            : KVObject.Collection();

        var result = new MixProcessor
        {
            Name = GetString(processor, "m_name"),
            NodeType = ProcessorNodeType(key),
            Channels = (int)GetInteger(processor, -1, "m_nChannels"),
            CrossFade = GetFloat(processor, 0.1f, "m_flxfade"),
            Desc = desc,
            EffectName = GetString(processor, "m_effectName") is { Length: > 0 } effect ? effect : GetString(desc, "m_effectName"),
            Subgraphs = [.. (TryGet(processor, out var subgraphs, "m_subgraphs") || TryGet(desc, out subgraphs, "m_subgraphs")
                ? subgraphs.Elements.Select(static name => (string)name)
                : [])],
            DebugId = UnsignedProperty(processor, "m_nDebugId"),
        };

        foreach (var (name, value) in processor)
        {
            var parameter = name.StartsWith("m_outParam", StringComparison.Ordinal) ? name["m_outParam".Length..]
                : name.StartsWith("m_param", StringComparison.Ordinal) ? name["m_param".Length..]
                : null;

            if (parameter == null || value.ValueType != KVValueType.Collection)
            {
                continue;
            }

            // Inputs are bound with a bare offset, the others with an offset inside a parameter
            if (!value.TryGetValue("m_offset", out var offset))
            {
                offset = value;
            }

            if (offset.ValueType != KVValueType.Collection || !offset.ContainsKey("category"))
            {
                continue;
            }

            var category = GetString(offset, "category");
            var index = (int)offset.GetIntegerProperty("index");

            if (category == "HEAP_OFFSET")
            {
                result.Parameters[parameter] = index;
            }
            else if (category == "INPUT_INDEX")
            {
                result.InputParameters[parameter] = index;
            }
        }

        return result;
    }

    /// <summary>
    /// Maps a processor type (VPROCESSOR_FILTER) or class (CVMixFilterProcessorDesc), stripped of its affixes, to the node that creates it.
    /// </summary>
    private static string ProcessorNodeType(string key) => key.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
    {
        "FILTER" => "audio.Filter",
        "DELAY" => "audio.Delay",
        "MODDELAY" => "audio.ModDelay",
        "FLANGER" => "audio.Flanger",
        "DYNAMICS" => "audio.Dynamics",
        "DYNAMICSCOMPRESSOR" => "audio.Compressor",
        "DYNAMICS3BAND" => "audio.Dynamics3Band",
        "DUALCOMPRESSOR" => "audio.DualCompressor",
        "EQ8" => "audio.EQ8",
        "FREEVERB" => "audio.Freeverb",
        "PLATEVERB" or "PLATEREVERB" => "audio.Plateverb",
        "BOXVERB" => "audio.ShoeboxReverb",
        "BOXVERB2" => "audio.ShoeboxReverb2",
        "DIFFUSOR" => "audio.Diffusor",
        "UTILITY" => "audio.Utility",
        "SHAPER" => "audio.Shaper",
        "OSC" => "audio.Osc",
        "PANNER" => "audio.Panner",
        "AUTOFILTER" => "audio.AutoFilter",
        "CONVOLUTION" => "audio.Convolution",
        "STEAMAUDIOHRTF" => "audio.SteamAudioSource",
        "STEAMAUDIOPATHING" => "audio.SteamAudioPathing",
        "STEAMAUDIOREVERB" => "audio.SteamAudioReverb",
        "STEAMAUDIODIRECT" => "audio.SteamAudioDirect",
        "STEAMAUDIOHYBRIDREVERB" => "audio.SteamAudioHybridReverb",
        "SUBGRAPHSWITCH" => "sys.SubgraphSwitch",
        "PRESETDSP" => "audio.Processor",
        "EFFECTCHAIN" => "audio.EffectChain",
        "PITCHSHIFT" or "RTPITCH" => "audio.PitchShift",
        "VOCODER" => "audio.Vocoder",
        "ENVELOPE" => "audio.Envelope",
        "STEREODELAY" => "audio.StereoDelay",
        _ => "audio.Processor",
    };
}
