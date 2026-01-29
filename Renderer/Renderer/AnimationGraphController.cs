using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    class GraphContext
    {
        public required AnimationGraphController Controller { get; set; }
        public required GraphNode[] Nodes { get; set; }
    }

    partial class BoolValueNode { public virtual bool Evaluate(GraphContext ctx) => throw new NotImplementedException(); }
    partial class ConstBoolNode { public override bool Evaluate(GraphContext ctx) => Value; }
    partial class ControlParameterBoolNode { public override bool Evaluate(GraphContext ctx) => ctx.Controller.BoolParameters[ctx.Controller.ParameterNames[NodeIdx]]; }
    partial class AndNode { public override bool Evaluate(GraphContext ctx) => ConditionNodeIndices.All(conditionIdx => ((BoolValueNode)ctx.Nodes[conditionIdx]).Evaluate(ctx)); }
    partial class OrNode { public override bool Evaluate(GraphContext ctx) => ConditionNodeIndices.Any(conditionIdx => ((BoolValueNode)ctx.Nodes[conditionIdx]).Evaluate(ctx)); }

    partial class FloatComparisonNode
    {
        public override bool Evaluate(GraphContext ctx)
        {
            var inputValue = ((FloatValueNode)ctx.Nodes[InputValueNodeIdx]).Evaluate(ctx);

            var comparisonValue = ComparisonValue;
            if (ComparandValueNodeIdx != -1)
            {
                var inputNode = (FloatValueNode)ctx.Nodes[ComparandValueNodeIdx];
                comparisonValue = inputNode.Evaluate(ctx);
            }

            return Comparison switch
            {
                FloatComparisonNode__Comparison.LessThan => inputValue < ComparisonValue,
                FloatComparisonNode__Comparison.LessThanEqual => inputValue <= ComparisonValue,
                FloatComparisonNode__Comparison.GreaterThan => inputValue > ComparisonValue,
                FloatComparisonNode__Comparison.GreaterThanEqual => inputValue >= ComparisonValue,
                FloatComparisonNode__Comparison.NearEqual => MathF.Abs(inputValue - ComparisonValue) <= Epsilon,
                _ => false,
            };
        }
    }

    partial class FloatValueNode { public virtual float Evaluate(GraphContext ctx) => throw new NotImplementedException(); }
    partial class IDValueNode { public virtual GlobalSymbol Evaluate(GraphContext ctx) => throw new NotImplementedException(); }
    partial class VectorValueNode { public virtual Vector3 Evaluate(GraphContext ctx) => throw new NotImplementedException(); }

    partial class ParameterizedClipSelectorNode
    {
        public ClipReferenceNode SelectOption(GraphContext ctx)
        {
            var parameterNode = (FloatValueNode)ctx.Nodes[ParameterNodeIdx];
            var options = OptionNodeIndices.Select(idx => (ClipReferenceNode)ctx.Nodes[idx]).ToArray();
            var selectedIndex = parameterNode.Evaluate(ctx) switch
            {
                var v when v <= 0.0f => 0,
                var v when v >= options.Length - 1 => options.Length - 1,
                var v => (int)MathF.Round(v)
            };

            if (HasWeightsSet)
            {
                // ?
            }

            return options[selectedIndex];
        }
    }
}

namespace ValveResourceFormat.Renderer
{
    public class AnimationGraphController : AnimationController
    {
        public Skeleton Skeleton2 { get; }
        public Animation Animation { get; }

        AnimationController SequencePlayback { get; }

        private readonly int[] nmSkelToModelSkeleton;
        private readonly Dictionary<string, string?> boneRemapDebugNames = [];


        public string Name { get; set; }
        public Dictionary<string, bool> BoolParameters { get; } = [];
        public Dictionary<string, float> FloatParameters { get; } = [];
        public Dictionary<string, string> IdParameters { get; } = [];
        // target
        public Dictionary<string, Vector4> VectorParameters { get; } = [];
        public string[] ParameterNames { get; private set; }

        private readonly HashSet<string> KnownIds = [];
        private readonly Dictionary<string, HashSet<string>> IdOptions = [];
        public IEnumerable<string> GetParameterIdOptions(string parameterName)
        {
            if (IdOptions.TryGetValue(parameterName, out var options))
            {
                return options;
            }

            return [];
        }

        public AnimationGraphController(Skeleton modelSkeleton, NmGraphDefinition graphDefinition, GameFileLoader fileLoader)
            : base(modelSkeleton, [])
        {
            var graph = graphDefinition.Data;
            Debug.Assert(graph != null, "Animation graph definition data is null.");

            var variationId = graph.GetProperty<string>("m_variationID");
            Name = $"{Path.GetFileNameWithoutExtension(graphDefinition.Resource.FileName)} ({variationId})";

            // Load animated skeleton
            var skeletonName = graph.GetProperty<string>("m_skeleton");
            var res = fileLoader.LoadFileCompiled(skeletonName) ?? throw new InvalidDataException($"Skeleton file '{skeletonName}' could not be found.");
            Skeleton2 = Skeleton.FromSkeletonData(((BinaryKV3)res.DataBlock!).Data);


            IterateGraph(graph);

            // Load first clip
            var resources = graph.GetArray<string>("m_resources");
            Debug.Assert(resources.Length > 0, "No resources in animation graph definition.");
            var firstClipName = resources[0];

            var clip = fileLoader.LoadFileCompiled(firstClipName) ?? throw new InvalidDataException($"Animation clip file '{firstClipName}' could not be found.");

            Animation = new Animation((AnimationClip)clip.DataBlock!);

            // test sequence playback for the ag2 anim
            SequencePlayback = new AnimationController(Skeleton2, []);
            SequencePlayback.SetAnimation(Animation);

            {
                // Build bone remap table
                var sourceBoneCount = Skeleton2.Bones.Length;
                var destinationBoneCount = Skeleton.Bones.Length;
                nmSkelToModelSkeleton = new int[destinationBoneCount];
                var nameToIndex = new Dictionary<uint, int>(sourceBoneCount);

                for (var i = 0; i < sourceBoneCount; i++)
                {
                    var name = Skeleton2.Bones[i].Name;
                    nameToIndex[StringToken.Store(name)] = i;
                }

                for (var i = 0; i < destinationBoneCount; i++)
                {
                    var name = Skeleton.Bones[i].Name;
                    var hash = StringToken.Store(name);

                    nmSkelToModelSkeleton[i] = -1;
                    boneRemapDebugNames[name] = null;

                    if (nameToIndex.TryGetValue(hash, out var idx))
                    {
                        nmSkelToModelSkeleton[i] = idx;
                        boneRemapDebugNames[name] = Skeleton2.Bones[idx].Name;
                    }
                }
            }
        }

        private void IterateGraph(KVObject graph)
        {
            // figure out parameters
            ParameterNames = graph.GetArray<string>("m_controlParameterIDs");
            var nodes = graph.GetArray<KVObject>("m_nodes");

            // copied from node viewer
            // todo: unduplicate
            string GetType(int nodeIdx)
            {
                var className = nodes[nodeIdx].GetStringProperty("_class");
                const string Prefix = "CNm";
                const string Suffix = "Node::CDefinition";
                var @type = className[Prefix.Length..^Suffix.Length];
                return @type;
            }

            var i = 0;
            foreach (var node in nodes)
            {
                var type = GetType(i);

                const string ControlParameterClassPreffix = "ControlParameter";
                if (type.StartsWith(ControlParameterClassPreffix, StringComparison.Ordinal))
                {
                    var parameterName = parameterIds[i];
                    var parameterType = type[ControlParameterClassPreffix.Length..];

                    switch (parameterType)
                    {
                        case "Bool": BoolParameters[parameterName] = false; break;
                        case "Float": FloatParameters[parameterName] = 0.0f; break;
                        case "ID": IdParameters[parameterName] = string.Empty; break;
                        case "Vector": VectorParameters[parameterName] = Vector4.Zero; break;
                        default: throw new InvalidDataException($"Unknown control parameter type '{parameterType}' in animation graph.");
                    }
                }
                else if (type == "IDComparison")
                {
                    var parameterName = parameterIds[node.GetInt32Property("m_nInputValueNodeIdx")];
                    var idsToCompare = node.GetArray<string>("m_comparisionIDs");

                    KnownIds.UnionWith(idsToCompare);
                    IdOptions.TryAdd(parameterName, [.. idsToCompare]);
                    IdOptions[parameterName].UnionWith(idsToCompare);
                }

                i++;
            }
        }

        public override void SetAnimation(Animation? animation)
        {
            // Ignore
        }

        public override bool Update(float timeStep)
        {
            var updated = SequencePlayback.Update(timeStep);

            if (updated)
            {
                for (var i = 0; i < Pose.Length; i++)
                {
                    var srcIdx = nmSkelToModelSkeleton[i];
                    if (srcIdx >= 0 && srcIdx < SequencePlayback.Pose.Length)
                    {
                        Pose[i] = SequencePlayback.Pose[srcIdx];
                        continue;
                    }

                    // If bone not found, fallback to bind pose or leave unchanged
                    // Pose[i] = SequencePlayback.BindPose.Length > i ? SequencePlayback.BindPose[i] : Pose[i];
                }
            }

            return updated;
        }
    }
}
