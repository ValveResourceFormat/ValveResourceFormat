using System.Linq;
using ValveResourceFormat.Renderer.AnimLib;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// The span of values a float parameter is read over, from the values the graph compares it with
    /// and the ranges it blends and remaps over.
    /// </summary>
    /// <param name="Min">The lowest value the graph reads, never above zero.</param>
    /// <param name="Max">The highest value the graph reads.</param>
    /// <param name="IsWholeNumber">Whether the graph only compares the value with whole numbers, like a count.</param>
    /// <param name="IsDiscrete">Whether the value picks between a few whole-number states, as an option index or in equality tests.</param>
    public readonly record struct FloatParameterRange(float Min, float Max, bool IsWholeNumber, bool IsDiscrete);

    public partial class AnimationGraph
    {
        private sealed class FloatUsage
        {
            public float Min = float.MaxValue;
            public float Max = float.MinValue;
            public bool IsContinuous;
            public bool HasEquality;
            public bool IsIndex;
            public bool AllWhole = true;

            public void Add(float value)
            {
                Min = MathF.Min(Min, value);
                Max = MathF.Max(Max, value);
                AllWhole &= value == MathF.Round(value);
            }

            public void Merge(FloatUsage other)
            {
                Min = MathF.Min(Min, other.Min);
                Max = MathF.Max(Max, other.Max);
                IsContinuous |= other.IsContinuous;
                HasEquality |= other.HasEquality;
                IsIndex |= other.IsIndex;
                AllWhole &= other.AllWhole;
            }
        }

        // Equality tests over more whole values than this read as a continuous quantity
        private const int MaxDiscreteValues = 32;

        private readonly Dictionary<string, HashSet<string>> idOptions = [];
        private readonly Dictionary<string, FloatUsage> floatUsage = [];

        /// <summary>
        /// Gets the known values for an ID parameter: the IDs the graph and its child graphs compare it with,
        /// select on or map from, including through cached, virtual and switched values.
        /// </summary>
        public IEnumerable<string> GetParameterIdOptions(string parameterName)
        {
            var options = new HashSet<string>();

            if (idOptions.TryGetValue(parameterName, out var own))
            {
                options.UnionWith(own);
            }

            foreach (var childGraph in ChildGraphs)
            {
                if (childGraph != null)
                {
                    options.UnionWith(childGraph.GetParameterIdOptions(parameterName));
                }
            }

            return options;
        }

        /// <summary>
        /// Gets the span of values the graph and its child graphs read a float parameter over, or
        /// <see langword="null"/> when nothing in the graph bounds it.
        /// </summary>
        public FloatParameterRange? GetFloatParameterRange(string parameterName)
        {
            var usage = new FloatUsage();
            usage.Add(0f);

            if (!MergeFloatUsage(parameterName, usage) || usage.Max <= usage.Min)
            {
                return null;
            }

            var isWholeNumber = usage.AllWhole && !usage.IsContinuous;
            var isDiscrete = isWholeNumber && (usage.HasEquality || usage.IsIndex) && usage.Max - usage.Min < MaxDiscreteValues;
            return new FloatParameterRange(usage.Min, usage.Max, isWholeNumber, isDiscrete);
        }

        private bool MergeFloatUsage(string parameterName, FloatUsage usage)
        {
            var found = false;

            if (floatUsage.TryGetValue(parameterName, out var own))
            {
                usage.Merge(own);
                found = true;
            }

            foreach (var childGraph in ChildGraphs)
            {
                if (childGraph != null)
                {
                    found |= childGraph.MergeFloatUsage(parameterName, usage);
                }
            }

            return found;
        }

        private void CollectParameterUsage(GraphNode[] nodes)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case IDComparisonNode comparison:
                        AddIdOptions(nodes, comparison.InputValueNodeIdx, comparison.ComparisionIDs);
                        break;
                    case IDBasedSelectorNode selector:
                        AddIdOptions(nodes, selector.ParameterNodeIdx, selector.OptionIDs);
                        break;
                    case IDBasedClipSelectorNode selector:
                        AddIdOptions(nodes, selector.ParameterNodeIdx, selector.OptionIDs);
                        break;
                    case IDToFloatNode idToFloat:
                        AddIdOptions(nodes, idToFloat.InputValueNodeIdx, idToFloat.IDs);
                        break;
                    case BoneMaskSelectorNode maskSelector:
                        AddIdOptions(nodes, maskSelector.ParameterValueNodeIdx, maskSelector.ParameterValues);
                        break;

                    case FloatComparisonNode comparison when comparison.ComparandValueNodeIdx < 0:
                        AddFloatUsage(nodes, comparison.InputValueNodeIdx, usage =>
                        {
                            usage.Add(comparison.ComparisonValue);
                            usage.HasEquality |= comparison.Comparison == FloatComparisonNode.ComparisonType.NearEqual;
                        });
                        break;
                    case FloatRangeComparisonNode rangeComparison:
                        AddFloatRange(nodes, rangeComparison.InputValueNodeIdx, rangeComparison.Range.Min, rangeComparison.Range.Max);
                        break;
                    case FloatRemapNode remap:
                        AddFloatRange(nodes, remap.InputValueNodeIdx, remap.InputRange.Begin, remap.InputRange.End);
                        break;
                    case FloatClampNode clamp:
                        AddFloatRange(nodes, clamp.InputValueNodeIdx, clamp.ClampRange.Min, clamp.ClampRange.Max);
                        break;
                    case Blend1DNode blend:
                        AddFloatRange(nodes, blend.InputParameterValueNodeIdx, blend.Parameterization.ParameterRange.Min, blend.Parameterization.ParameterRange.Max);
                        break;
                    case ParameterizedSelectorNode selector:
                        AddOptionIndex(nodes, selector.ParameterNodeIdx, selector.OptionNodeIndices.Length, selector.HasWeightsSet ? selector.OptionWeights : null);
                        break;
                    case ParameterizedClipSelectorNode selector:
                        AddOptionIndex(nodes, selector.ParameterNodeIdx, selector.OptionNodeIndices.Length, selector.HasWeightsSet ? selector.OptionWeights : null);
                        break;
                    case AnimationPoseNode pose:
                        var timeRange = pose.InputTimeRemapRange.IsSet ? pose.InputTimeRemapRange : new Range(0f, 1f);
                        AddFloatRange(nodes, pose.PoseTimeValueNodeIdx, timeRange.Min, timeRange.Max);
                        break;
                    case FloatCurveNode curve:
                        AddFloatRange(nodes, curve.InputValueNodeIdx, curve.Curve.DomainMin.X, curve.Curve.DomainMax.X);
                        break;
                    case SpeedScaleNode speedScale:
                        // A playback rate multiplier, the graph itself does not bound it
                        AddFloatRange(nodes, speedScale.InputValueNodeIdx, 0f, 2f);
                        break;
                    case Blend2DNode blend:
                        foreach (var value in blend.Values)
                        {
                            AddFloatRange(nodes, blend.InputParameterNodeIdx0, value.X, value.X);
                            AddFloatRange(nodes, blend.InputParameterNodeIdx1, value.Y, value.Y);
                        }

                        break;
                }
            }
        }

        private void AddIdOptions(GraphNode[] nodes, int nodeIdx, GlobalSymbol[] ids)
        {
            foreach (var parameterName in SourceParameters(nodes, nodeIdx))
            {
                if (!idOptions.TryGetValue(parameterName, out var options))
                {
                    options = [];
                    idOptions[parameterName] = options;
                }

                foreach (var id in ids)
                {
                    if (id.IsValid)
                    {
                        options.Add(id.Name);
                    }
                }
            }
        }

        // Parameterized selectors wrap the whole part of the value around the weighted option list
        private void AddOptionIndex(GraphNode[] nodes, int nodeIdx, int optionCount, byte[]? weights)
        {
            var slots = weights != null ? weights.Sum(static weight => weight) : optionCount;

            if (slots == 0)
            {
                return;
            }

            AddFloatUsage(nodes, nodeIdx, usage =>
            {
                usage.Add(0f);
                usage.Add(slots - 1);
                usage.IsIndex = true;
            });
        }

        private void AddFloatRange(GraphNode[] nodes, int nodeIdx, float min, float max)
        {
            AddFloatUsage(nodes, nodeIdx, usage =>
            {
                usage.Add(min);
                usage.Add(max);
                usage.IsContinuous = true;
            });
        }

        private void AddFloatUsage(GraphNode[] nodes, int nodeIdx, Action<FloatUsage> add)
        {
            foreach (var parameterName in SourceParameters(nodes, nodeIdx))
            {
                if (!floatUsage.TryGetValue(parameterName, out var usage))
                {
                    usage = new FloatUsage();
                    floatUsage[parameterName] = usage;
                }

                add(usage);
            }
        }

        /// <summary>The control parameters whose value reaches a node unchanged, or through a switch between them.</summary>
        private List<string> SourceParameters(GraphNode[] nodes, int nodeIdx)
        {
            List<string> parameters = [];
            CollectSourceParameters(nodes, nodeIdx, parameters, depth: 0);
            return parameters;
        }

        private void CollectSourceParameters(GraphNode[] nodes, int nodeIdx, List<string> parameters, int depth)
        {
            // Value nodes form a tree in valid graphs, the depth cap only guards against malformed ones
            if (nodeIdx < 0 || nodeIdx >= nodes.Length || depth > 16)
            {
                return;
            }

            switch (nodes[nodeIdx])
            {
                case ControlParameterIDNode or ControlParameterFloatNode when nodeIdx < ParameterNames.Length:
                    parameters.Add(ParameterNames[nodeIdx]);
                    break;
                case CachedIDNode cached:
                    CollectSourceParameters(nodes, cached.InputValueNodeIdx, parameters, depth + 1);
                    break;
                case CachedFloatNode cached:
                    CollectSourceParameters(nodes, cached.InputValueNodeIdx, parameters, depth + 1);
                    break;
                case VirtualParameterIDNode virtualParameter:
                    CollectSourceParameters(nodes, virtualParameter.ChildNodeIdx, parameters, depth + 1);
                    break;
                case VirtualParameterFloatNode virtualParameter:
                    CollectSourceParameters(nodes, virtualParameter.ChildNodeIdx, parameters, depth + 1);
                    break;
                case IDSwitchNode idSwitch:
                    CollectSourceParameters(nodes, idSwitch.TrueValueNodeIdx, parameters, depth + 1);
                    CollectSourceParameters(nodes, idSwitch.FalseValueNodeIdx, parameters, depth + 1);
                    break;
                case FloatSwitchNode floatSwitch:
                    CollectSourceParameters(nodes, floatSwitch.TrueValueNodeIdx, parameters, depth + 1);
                    CollectSourceParameters(nodes, floatSwitch.FalseValueNodeIdx, parameters, depth + 1);
                    break;
                case FloatEaseNode ease:
                    CollectSourceParameters(nodes, ease.InputValueNodeIdx, parameters, depth + 1);
                    break;
                case FloatSpringNode spring:
                    CollectSourceParameters(nodes, spring.InputValueNodeIdx, parameters, depth + 1);
                    break;
            }
        }
    }
}
