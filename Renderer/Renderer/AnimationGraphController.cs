using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;


namespace ValveResourceFormat.Renderer
{
    public class AnimationGraphController : AnimationController
    {
        public Skeleton Skeleton2 { get; }
        public Animation? Animation => Sequences.Length > 0 ? Sequences[0].ActiveAnimation : null;

        public AnimationController[] Sequences { get; private set; } = [];

        private readonly int[] nmSkelToModelSkeleton;
        private readonly Dictionary<string, string?> boneRemapDebugNames = [];

        AnimLib.GraphContext Graph { get; set; }

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
            Debug.Assert(ParameterNames != null);

            // Load all clips
            var resources = graph.GetArray<string>("m_resources");
            Sequences = new AnimationController[resources.Length];

            for (var ri = 0; ri < resources.Length; ri++)
            {
                var clipName = resources[ri];
                var clipFile = fileLoader.LoadFileCompiled(clipName) ?? throw new InvalidDataException($"Animation clip file '{clipName}' could not be found.");

                if (clipFile.ResourceType == ResourceType.NmClip)
                {
                    var clipAnim = new Animation((AnimationClip)clipFile.DataBlock!);

                    var ctrl = new AnimationController(Skeleton2, []);
                    ctrl.SetAnimation(clipAnim);
                    Sequences[ri] = ctrl;
                }
                else if (clipFile.ResourceType == ResourceType.NmGraph)
                {
                    var subGraphDef = (NmGraphDefinition)clipFile.DataBlock!;
                    var subGraph = subGraphDef.Data;

                    Debug.Assert(subGraph != null, "Subgraph definition data is null.");

                    var subGraphController = new AnimationGraphController(modelSkeleton, subGraphDef, fileLoader);
                    Sequences[ri] = subGraphController;
                }
            }

            Graph = new AnimLib.GraphContext(graph, Skeleton2, this);

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
                    var parameterName = ParameterNames[i];
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
                    var parameterName = ParameterNames[node.GetInt32Property("m_nInputValueNodeIdx")];
                    var idsToCompare = node.GetArray<string>("m_comparisionIDs");

                    KnownIds.UnionWith(idsToCompare);
                    IdOptions.TryAdd(parameterName, [.. idsToCompare]);
                    IdOptions[parameterName].UnionWith(idsToCompare);
                }

                i++;
            }
        }

        private int SequenceIndex { get; set; } = -1;
        public override void SetAnimation(Animation? animation)
        {
            if (animation == null)
            {
                SequenceIndex = -1;
                return;
            }

            SequenceIndex = Array.FindIndex(Sequences, a => a.ActiveAnimation == animation);
        }

        public override bool Update(float timeStep)
        {
            var graphPose = Graph.Update(timeStep);

            for (var i = 0; i < Pose.Length; i++)
            {
                var srcIdx = nmSkelToModelSkeleton[i];
                if (srcIdx >= 0 && srcIdx < graphPose.Pose.Length)
                {
                    Pose[i] = graphPose.Pose[srcIdx].ToMatrix();
                    continue;
                }

                // If bone not found, fallback to bind pose or leave unchanged
                // Pose[i] = Sequences[0].BindPose.Length > i ? Sequences[0].BindPose[i] : Pose[i];
            }

            return true;

            var sequence = (SequenceIndex >= 0 && SequenceIndex < Sequences.Length) ? Sequences[SequenceIndex] : null;
            if (sequence == null)
            {
                return false;
            }

            var updated = sequence.Update(timeStep);
            if (updated)
            {
                for (var i = 0; i < Pose.Length; i++)
                {
                    var srcIdx = nmSkelToModelSkeleton[i];
                    if (srcIdx >= 0 && srcIdx < sequence.Pose.Length)
                    {
                        Pose[i] = sequence.Pose[srcIdx];
                        continue;
                    }

                    // If bone not found, fallback to bind pose or leave unchanged
                    // Pose[i] = Sequences[0].BindPose.Length > i ? Sequences[0].BindPose[i] : Pose[i];
                }
            }

            return updated;
        }
    }
}
