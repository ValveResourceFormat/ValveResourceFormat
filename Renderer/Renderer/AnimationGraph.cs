using System.Diagnostics;
using System.IO;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Runtime instance of an animation graph (.vnmgraph). Evaluates the graph's node network on the
    /// graph's NM skeleton each frame and produces a model-space pose. Play it on a model through
    /// <see cref="AnimationController.SetAnimationGraph"/>, which routes it to the
    /// <see cref="AnimationPlayer"/> of the matching external skeleton.
    /// </summary>
    public class AnimationGraph
    {
        /// <summary>Gets the NM skeleton (.vnmskel) this graph animates.</summary>
        public Skeleton Skeleton { get; }

        /// <summary>Gets the resource name of the graph's skeleton, matching the external skeleton registration key.</summary>
        public string SkeletonName { get; }

        /// <summary>Gets the display name of the graph (file name and variation).</summary>
        public string Name { get; }

        /// <summary>Gets the boolean control parameters by name.</summary>
        public Dictionary<string, bool> BoolParameters { get; } = [];

        /// <summary>Gets the float control parameters by name.</summary>
        public Dictionary<string, float> FloatParameters { get; } = [];

        /// <summary>Gets the ID (symbol) control parameters by name.</summary>
        public Dictionary<string, string> IdParameters { get; } = [];

        /// <summary>Gets the vector control parameters by name.</summary>
        public Dictionary<string, Vector4> VectorParameters { get; } = [];

        /// <summary>
        /// Gets the target (bone transform) control parameters by name. A <see langword="null"/> value
        /// leaves the target unset, which turns off the IK reading it.
        /// </summary>
        public Dictionary<string, FrameBone?> TargetParameters { get; } = [];

        /// <summary>
        /// Gets, for target and vector parameters read by IK or look-at nodes, the value that would leave
        /// the pose as animated. Refreshed on every update.
        /// </summary>
        public Dictionary<string, GraphParameterHint> ParameterHints { get; } = [];

        /// <summary>Gets the transform the graph treats as its world space, the root motion accumulated so far.</summary>
        public FrameBone WorldTransform => graphContext.WorldTransform;

        /// <summary>Gets the control parameter names, indexed by control parameter node index.</summary>
        public string[] ParameterNames { get; private set; } = [];

        /// <summary>
        /// Gets or sets whether clamped (non-looping) clips loop anyway. Not authentic — graphs rely
        /// on the game re-triggering actions — but useful in a viewer to keep animations moving.
        /// </summary>
        public bool ForceLoopingClips { get; set; }

        /// <summary>
        /// The graph's data slots: one entry per resource reference. Clip resources get a sampleable
        /// <see cref="GraphClip"/>; other resource types are null.
        /// </summary>
        internal GraphClip?[] DataSlots { get; } = [];

        /// <summary>
        /// Referenced child graphs, index-aligned with <see cref="DataSlots"/>: one instance per
        /// graph resource reference, null for other resource types (or on a recursive reference).
        /// </summary>
        internal AnimationGraph?[] ChildGraphs { get; } = [];

        /// <summary>The AnimLib view of the skeleton (reference pose, bone masks).</summary>
        internal AnimLib.Skeleton AnimLibSkeleton { get; }

        /// <summary>Parent-space reference (bind) pose of the NM skeleton.</summary>
        internal FrameBone[] ParentSpaceReferencePose { get; }

        private readonly AnimLib.GraphContext graphContext;

        /// <summary>The graph evaluation context, exposed for debug tooling and tests.</summary>
        internal AnimLib.GraphContext Context => graphContext;

        // Bool parameters that were signaled as a one-shot and must be reset to false after the next update.
        private readonly HashSet<string> signaledBoolParameters = [];
        private readonly object signalLock = new();

        private readonly Dictionary<string, HashSet<string>> idOptions = [];

        /// <summary>
        /// Gets the known values for an ID parameter, collected from the graph's IDComparison and
        /// ID-based selector nodes, including those of referenced child graphs. Useful for
        /// populating UI dropdowns.
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
        /// Loads the graph definition, its skeleton and all referenced animation clips.
        /// </summary>
        /// <param name="graphDefinition">The graph definition resource data.</param>
        /// <param name="fileLoader">Loader used to resolve the skeleton and clip resources.</param>
        public AnimationGraph(NmGraphDefinition graphDefinition, IFileLoader fileLoader)
            : this(graphDefinition, fileLoader, [])
        {
        }

        private AnimationGraph(NmGraphDefinition graphDefinition, IFileLoader fileLoader, HashSet<string> loadStack)
        {
            var graph = graphDefinition.Data.Root;
            Debug.Assert(graph != null, "Animation graph definition data is null.");

            var variationId = graph.GetProperty<string>("m_variationID");
            Name = $"{Path.GetFileNameWithoutExtension(graphDefinition.Resource?.FileName)} ({variationId})";

            // Load the animated skeleton
            SkeletonName = graph.GetProperty<string>("m_skeleton")
                ?? throw new InvalidDataException("Animation graph has no skeleton reference.");
            var res = fileLoader.LoadFileCompiled(SkeletonName) ?? throw new InvalidDataException($"Skeleton file '{SkeletonName}' could not be found.");
            var skeletonData = ((BinaryKV3)res.DataBlock!).Data.Root;
            Skeleton = Skeleton.FromSkeletonData(skeletonData);
            AnimLibSkeleton = new AnimLib.Skeleton(skeletonData);

            ParentSpaceReferencePose = new FrameBone[Skeleton.Bones.Length];
            for (var i = 0; i < Skeleton.Bones.Length; i++)
            {
                ParentSpaceReferencePose[i] = new FrameBone(Skeleton.Bones[i].Position, 1f, Skeleton.Bones[i].Angle);
            }

            CollectParameters(graph);

            // Load all clips and referenced child graphs. Slots must stay index-aligned with m_resources.
            var resources = graph.GetArray<string>("m_resources") ?? [];
            DataSlots = new GraphClip?[resources.Length];
            ChildGraphs = new AnimationGraph?[resources.Length];

            var graphResourceName = graphDefinition.Resource?.FileName ?? Name;
            loadStack.Add(graphResourceName);

            for (var ri = 0; ri < resources.Length; ri++)
            {
                var resourceName = resources[ri];
                var resourceFile = fileLoader.LoadFileCompiled(resourceName);

                if (resourceFile?.ResourceType == ResourceType.NmClip)
                {
                    var clipAnim = new ClipAnimation((AnimationClip)resourceFile.DataBlock!);
                    DataSlots[ri] = new GraphClip(clipAnim, Skeleton);
                }
                else if (resourceFile?.DataBlock is NmGraphDefinition childDefinition
                    && !loadStack.Contains(resourceFile.FileName ?? resourceName))
                {
                    ChildGraphs[ri] = new AnimationGraph(childDefinition, fileLoader, loadStack);
                }
            }

            loadStack.Remove(graphResourceName);

            graphContext = new AnimLib.GraphContext(graph, this);
        }

        /// <summary>
        /// Pulses a boolean control parameter as a one-shot "signal": the value reads as <c>true</c> for the
        /// next graph update, then is automatically reset to <c>false</c>. Use for trigger parameters
        /// (e.g. <c>action_reset</c>) that would otherwise re-fire every frame while held <c>true</c>.
        /// </summary>
        public void SignalBoolParameter(string name)
        {
            lock (signalLock)
            {
                BoolParameters[name] = true;
                signaledBoolParameters.Add(name);
            }
        }

        /// <summary>
        /// The root motion delta produced by the last update, in the character's local space: the new
        /// world transform is this delta concatenated onto the previous one.
        /// </summary>
        internal FrameBone RootMotionDelta { get; private set; } = FrameBone.Identity;

        /// <summary>
        /// Advances the graph by <paramref name="timeStep"/> seconds and returns the resulting
        /// parent-space pose on the NM skeleton.
        /// </summary>
        internal FrameBone[] Update(float timeStep, FrameBone worldTransform)
        {
            graphContext.WorldTransform = worldTransform;
            graphContext.WorldTransformInverse = worldTransform.Inverse();

            var result = graphContext.Update(timeStep);
            RootMotionDelta = result.RootMotionDelta;

            // Reset one-shot signaled bool parameters now that the graph has consumed them this frame.
            if (signaledBoolParameters.Count > 0)
            {
                lock (signalLock)
                {
                    foreach (var name in signaledBoolParameters)
                    {
                        BoolParameters[name] = false;
                    }

                    signaledBoolParameters.Clear();
                }
            }

            return result.Pose;
        }

        private void CollectParameters(KVObject graph)
        {
            ParameterNames = graph.GetArray<string>("m_controlParameterIDs") ?? [];
            var nodes = graph.GetArray<KVObject>("m_nodes");

            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                var className = node.GetStringProperty("_class");
                const string Prefix = "CNm";
                const string Suffix = "Node::CDefinition";
                var type = className[Prefix.Length..^Suffix.Length];

                const string ControlParameterClassPrefix = "ControlParameter";
                if (type.StartsWith(ControlParameterClassPrefix, StringComparison.Ordinal))
                {
                    var parameterName = ParameterNames[i];
                    var parameterType = type[ControlParameterClassPrefix.Length..];

                    switch (parameterType)
                    {
                        case "Bool": BoolParameters[parameterName] = false; break;
                        case "Float": FloatParameters[parameterName] = 0.0f; break;
                        case "ID": IdParameters[parameterName] = string.Empty; break;
                        case "Vector": VectorParameters[parameterName] = Vector4.Zero; break;
                        case "Target": TargetParameters[parameterName] = null; break;
                        default: throw new InvalidDataException($"Unknown control parameter type '{parameterType}' in animation graph.");
                    }
                }
                else if (type == "IDComparison")
                {
                    CollectIdOptions(node.GetInt32Property("m_nInputValueNodeIdx"), node.GetArray<string>("m_comparisionIDs"));
                }
                else if (type is "IDBasedSelector" or "IDBasedClipSelector")
                {
                    CollectIdOptions(node.GetInt32Property("m_nParameterNodeIdx"), node.GetArray<string>("m_optionIDs"));
                }
                else if (type == "IDToFloat")
                {
                    CollectIdOptions(node.GetInt32Property("m_nInputValueNodeIdx"), node.GetArray<string>("m_IDs"));
                }
                else if (type == "BoneMaskSelector")
                {
                    CollectIdOptions(node.GetInt32Property("m_parameterValueNodeIdx"), node.GetArray<string>("m_parameterValues"));
                }
            }
        }

        private void CollectIdOptions(int parameterNodeIdx, string[]? ids)
        {
            if (parameterNodeIdx < 0 || parameterNodeIdx >= ParameterNames.Length || ids == null || ids.Length == 0)
            {
                return;
            }

            var parameterName = ParameterNames[parameterNodeIdx];
            idOptions.TryAdd(parameterName, []);
            idOptions[parameterName].UnionWith(ids);
        }

        /// <summary>(Re)initializes the graph's node tree at the given time.</summary>
        internal void ResetGraphState(AnimLib.SyncTrackTime initTime = default)
        {
            graphContext.ResetGraphState(initTime);
            RootMotionDelta = FrameBone.Identity;
        }
    }

    /// <summary>
    /// The value of a target or vector parameter that leaves the pose as animated.
    /// </summary>
    /// <param name="Transform">The value, in the space the parameter is read in. Vectors use the position only.</param>
    /// <param name="IsWorldSpace">Whether the parameter is read in the graph world space rather than in character space.</param>
    public readonly record struct GraphParameterHint(FrameBone Transform, bool IsWorldSpace);

    /// <summary>
    /// An animation clip bound to a graph data slot, sampleable into a parent-space pose on the
    /// graph's NM skeleton. Each clip has its own frame cache because
    /// <see cref="AnimationFrameCache"/> caches by frame index without keying on the animation.
    /// </summary>
    internal class GraphClip
    {
        /// <summary>The clip animation backing this slot.</summary>
        public ClipAnimation Animation { get; }

        /// <summary>The duration of the clip in seconds.</summary>
        public float Duration => Animation.Duration;

        /// <summary>The number of frames in the clip.</summary>
        public int FrameCount => Animation.FrameCount;

        /// <summary>The clip's sync track, used to align it with other clips.</summary>
        public AnimLib.SyncTrack SyncTrack { get; }

        /// <summary>The clip's root motion track.</summary>
        public AnimLib.RootMotionData RootMotion { get; }

        private readonly AnimationFrameCache frameCache;

        public GraphClip(ClipAnimation animation, Skeleton skeleton)
        {
            Animation = animation;
            frameCache = new AnimationFrameCache(skeleton, []);

            var clipData = animation.Clip.Data.Root;
            var syncTrackData = clipData.GetProperty<KVObject>("m_syncTrack");
            SyncTrack = syncTrackData != null ? new AnimLib.SyncTrack(syncTrackData) : AnimLib.SyncTrack.Default;
            RootMotion = new AnimLib.RootMotionData(clipData.GetProperty<KVObject>("m_rootMotion") ?? new KVObject());
        }

        /// <summary>The root motion delta for a time range; handles a single loop.</summary>
        public FrameBone GetRootMotionDelta(float fromTime, float toTime) => RootMotion.GetDelta(fromTime, toTime);

        /// <summary>The root motion delta for a time range that does not loop.</summary>
        public FrameBone GetRootMotionDeltaNoLooping(float fromTime, float toTime) => RootMotion.GetDeltaNoLooping(fromTime, toTime);

        /// <summary>Converts a percentage through the clip into a frame time.</summary>
        public AnimLib.FrameTime GetFrameTime(float percentageThrough) => new(percentageThrough, FrameCount);

        /// <summary>The percentage through the clip at which a frame starts.</summary>
        public float GetPercentageThrough(int frameIndex) => FrameCount > 1 ? (float)frameIndex / (FrameCount - 1) : 0f;

        /// <summary>Samples the clip at an exact frame index into a parent-space pose.</summary>
        public void SamplePoseAtFrame(int frameIndex, FrameBone[] pose)
        {
            var frame = frameCache.GetFrame(Animation, frameIndex);
            CopyFrame(frame, pose);
        }

        /// <summary>Samples the clip at a normalized time in [0, 1] into a parent-space pose.</summary>
        public Frame SamplePoseAtPercentage(float cycle, FrameBone[] pose)
        {
            Debug.Assert(cycle >= 0f && cycle <= 1f);

            var time = cycle * Animation.Duration;

            // The interpolated lookup wraps its frame index (modulo FrameCount - 1) once the time
            // passes the last stored frame, sampling the first frame again. That wrap is for looping
            // playback; a clamped clip sampled in its final frame interval must hold the final frame.
            var lastFrameTime = (Animation.FrameCount - 1) / Animation.Fps;
            if (time >= lastFrameTime)
            {
                var lastFrame = frameCache.GetFrame(Animation, Animation.FrameCount - 1);
                CopyFrame(lastFrame, pose);
                return lastFrame;
            }

            var frame = frameCache.GetInterpolatedFrame(Animation, time);
            CopyFrame(frame, pose);
            return frame;
        }

        private static void CopyFrame(Frame frame, FrameBone[] pose)
        {
            var count = Math.Min(frame.Bones.Length, pose.Length);
            frame.Bones.AsSpan(0, count).CopyTo(pose);
        }
    }
}
