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
    public partial class AnimationGraph : IAnimationGraph
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

        // Targets that follow a bone of the pose being built, which take over from the parameter of the same name
        internal Dictionary<string, AnimLib.Target> BoneTargetParameters { get; } = [];

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
        /// Gets or sets whether clamped (non-looping) clips loop anyway. Not authentic, as graphs rely
        /// on actions being re-triggered, but useful in a viewer to keep animations moving.
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

        // Signaled bool parameters and the value each returns to after the next update
        private readonly Dictionary<string, bool> signaledBoolParameters = [];
        private readonly object signalLock = new();

        // A float curve event of one of this graph's clips, parsed at load
        internal ValveResourceFormat.Particles.Utils.PiecewiseCurve GetFloatCurve(NmClipEvent curveEvent)
        {
            foreach (var slot in DataSlots)
            {
                if (slot != null && slot.FloatCurves.TryGetValue(curveEvent, out var curve))
                {
                    return curve;
                }
            }

            return new AnimLib.FloatCurveEvent(curveEvent.Data).Curve;
        }

        /// <summary>
        /// Loads the graph definition, its skeleton and all referenced animation clips.
        /// </summary>
        /// <param name="graphDefinition">The graph definition resource data.</param>
        /// <param name="fileLoader">Loader used to resolve the skeleton and clip resources.</param>
        /// <exception cref="InvalidDataException">A graph of the tree animates a skeleton the loader cannot provide.</exception>
        public AnimationGraph(BinaryKV3 graphDefinition, IFileLoader fileLoader)
            : this(graphDefinition, LoadResources(graphDefinition, fileLoader), [])
        {
        }

        /// <summary>
        /// Loads the graph like the constructor does, or returns <see langword="null"/> when a graph of the tree
        /// animates a skeleton the loader cannot provide, as unshipped work in progress graphs do.
        /// </summary>
        /// <param name="graphDefinition">The graph definition resource data.</param>
        /// <param name="fileLoader">Loader used to resolve the skeleton and clip resources.</param>
        /// <param name="loadedClips">Clips already loaded elsewhere, such as by the model playing the graph, looked up by
        /// resource name and used instead of reading them again.</param>
        public static AnimationGraph? TryLoad(BinaryKV3 graphDefinition, IFileLoader fileLoader, Func<string, ClipAnimation?>? loadedClips = null)
        {
            var resources = GraphResources.Load(graphDefinition, fileLoader, loadedClips);
            return resources.MissingSkeleton == null ? new AnimationGraph(graphDefinition, resources, []) : null;
        }

        private static GraphResources LoadResources(BinaryKV3 graphDefinition, IFileLoader fileLoader)
        {
            var resources = GraphResources.Load(graphDefinition, fileLoader);

            if (resources.MissingSkeleton != null)
            {
                throw new InvalidDataException($"Skeleton file '{resources.MissingSkeleton}' could not be found.");
            }

            return resources;
        }

        private AnimationGraph(BinaryKV3 graphDefinition, GraphResources resources, HashSet<string> loadStack)
        {
            var graph = graphDefinition.Data.Root;
            Debug.Assert(graph != null, "Animation graph definition data is null.");

            var variationId = graph.GetProperty<string>("m_variationID");
            Name = $"{Path.GetFileNameWithoutExtension(graphDefinition.Resource?.FileName)} ({variationId})";

            SkeletonName = GraphResources.GetSkeletonName(graph);
            var skeleton = resources.Skeletons[SkeletonName];
            Skeleton = skeleton.Skeleton;
            AnimLibSkeleton = skeleton.AnimLibSkeleton;

            ParentSpaceReferencePose = new FrameBone[Skeleton.Bones.Length];
            for (var i = 0; i < Skeleton.Bones.Length; i++)
            {
                ParentSpaceReferencePose[i] = new FrameBone(Skeleton.Bones[i].Position, 1f, Skeleton.Bones[i].Angle);
            }

            CollectParameters(graph);

            // Clips and referenced child graphs, slots index-aligned with m_resources
            var resourceNames = graph.GetArray<string>("m_resources") ?? [];
            DataSlots = new GraphClip?[resourceNames.Length];
            ChildGraphs = new AnimationGraph?[resourceNames.Length];

            var graphResourceName = graphDefinition.Resource?.FileName ?? Name;
            loadStack.Add(graphResourceName);

            for (var ri = 0; ri < resourceNames.Length; ri++)
            {
                var resourceName = resourceNames[ri];

                if (resources.Clips.TryGetValue((resourceName, SkeletonName), out var clip))
                {
                    DataSlots[ri] = clip;
                }
                else if (resources.Files.GetValueOrDefault(resourceName) is { ResourceType: ResourceType.NmGraph, DataBlock: BinaryKV3 childDefinition } childFile
                    && !loadStack.Contains(childFile.FileName ?? resourceName))
                {
                    ChildGraphs[ri] = new AnimationGraph(childDefinition, resources, loadStack);
                }
            }

            loadStack.Remove(graphResourceName);

            signaledBoolParameters.EnsureCapacity(BoolParameters.Count);
            graphContext = new AnimLib.GraphContext(graph, this);
            CollectParameterHeuristics(graphContext.Nodes);
            SetNeutralFloatDefaults();
        }

        /// <summary>
        /// Pulses a boolean control parameter as a one-shot "signal": the value reads as <c>true</c> for the
        /// next graph update, then returns to the value it held. Use for trigger parameters
        /// (e.g. <c>action_reset</c>) that would otherwise re-fire every frame while held <c>true</c>.
        /// </summary>
        public void SignalBoolParameter(string name)
        {
            lock (signalLock)
            {
                signaledBoolParameters.TryAdd(name, BoolParameters[name]);
                BoolParameters[name] = true;
            }
        }

        /// <inheritdoc/>
        public AnimationAlgorithm Algorithm => AnimationAlgorithm.AnimGraph2;

        /// <summary>
        /// Gets or sets whether updates record the clips they sample and how long each graph took, including
        /// referenced graphs, for <see cref="SampledClips"/> and <see cref="GraphTimings"/>. Off by default.
        /// </summary>
        public bool RecordUpdateDetails
        {
            get => UpdateDetails != null;
            set
            {
                UpdateDetails = value ? new GraphUpdateDetails() : null;
                graphContext.UpdateDetails = UpdateDetails;
            }
        }

        /// <summary>Gets the clips the last update sampled, in sampling order, while <see cref="RecordUpdateDetails"/> is set.</summary>
        public IReadOnlyList<SampledClip> SampledClips => UpdateDetails?.SampledClips ?? (IReadOnlyList<SampledClip>)[];

        /// <summary>
        /// Gets how long the last update took for this graph, first, and each referenced graph it evaluated, in
        /// evaluation order, while <see cref="RecordUpdateDetails"/> is set.
        /// </summary>
        public IReadOnlyList<GraphTiming> GraphTimings => UpdateDetails?.GraphTimings ?? (IReadOnlyList<GraphTiming>)[];

        /// <summary>
        /// Gets the events the last update sampled, from the states and clips of this graph and of the graphs it
        /// references.
        /// </summary>
        public AnimLib.SampledEventsBuffer SampledEvents => graphContext.SampledEvents;

        internal GraphUpdateDetails? UpdateDetails { get; private set; }

        /// <inheritdoc/>
        public FrameBone RootMotionDelta { get; private set; } = FrameBone.Identity;

        /// <inheritdoc/>
        public FrameBone[] Update(float timeStep, FrameBone worldTransform)
        {
            var details = UpdateDetails;
            details?.Clear();
            var timing = details?.BeginTiming(Name, 0) ?? -1;

            graphContext.WorldTransform = worldTransform;
            graphContext.WorldTransformInverse = worldTransform.Inverse();

            AnimLib.GraphPoseNodeResult result;
            var previousUpdatingGraph = updatingGraph;
            updatingGraph = this;

            try
            {
                result = graphContext.Update(timeStep);
            }
            catch (Exception e) when (AttachState(e))
            {
                throw;
            }
            finally
            {
                updatingGraph = previousUpdatingGraph;
            }

            RootMotionDelta = result.RootMotionDelta;

            if (timing >= 0)
            {
                details!.EndTiming(timing);
            }

            // Reset one-shot signaled bool parameters now that the graph has consumed them this frame.
            if (signaledBoolParameters.Count > 0)
            {
                lock (signalLock)
                {
                    foreach (var (name, heldValue) in signaledBoolParameters)
                    {
                        BoolParameters[name] = heldValue;
                    }

                    signaledBoolParameters.Clear();
                }
            }

            return result.Pose;
        }

        [ThreadStatic]
        private static AnimationGraph? updatingGraph;

        /// <summary>The key under which a failing update stores <see cref="DescribeState()"/> in the exception data.</summary>
        public const string ExceptionDataKey = "Animation graph state";

        /// <summary>
        /// Gets the state of the graph being updated on the calling thread, for reports of a failure raised
        /// from inside the update, or <see langword="null"/> outside of one.
        /// </summary>
        public static string? DescribeUpdatingGraph() => updatingGraph?.DescribeState();

        // Runs as an exception filter, so the state is captured before the stack unwinds; never catches
        private bool AttachState(Exception exception)
        {
            try
            {
                exception.Data[ExceptionDataKey] = DescribeState();
            }
            catch (Exception describeException) when (describeException is not OutOfMemoryException)
            {
                // A broken graph must not hide the original failure
            }

            return false;
        }

        /// <summary>
        /// Describes the parameter values and the active states of this graph and its referenced graphs.
        /// </summary>
        public string DescribeState()
        {
            var output = new System.Text.StringBuilder(1024);
            DescribeState(output, string.Empty);
            return output.ToString();
        }

        private void DescribeState(System.Text.StringBuilder output, string indent)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;

            output.Append(culture, $"{indent}{Name}{Environment.NewLine}");

            foreach (var (name, value) in BoolParameters)
            {
                output.Append(culture, $"{indent}  {name} = {value}{Environment.NewLine}");
            }

            foreach (var (name, value) in FloatParameters)
            {
                output.Append(culture, $"{indent}  {name} = {value}{Environment.NewLine}");
            }

            foreach (var (name, value) in IdParameters)
            {
                output.Append(culture, $"{indent}  {name} = \"{value}\"{Environment.NewLine}");
            }

            foreach (var (name, value) in VectorParameters)
            {
                output.Append(culture, $"{indent}  {name} = ({value.X}, {value.Y}, {value.Z}){Environment.NewLine}");
            }

            foreach (var (name, value) in TargetParameters)
            {
                var text = value is { } target ? $"{target.Position} {target.Angle}" : "unset";
                output.Append(culture, $"{indent}  {name} = {text}{Environment.NewLine}");
            }

            graphContext.DescribeActiveStates(output, indent + "  ");

            foreach (var childGraph in ChildGraphs)
            {
                if (childGraph?.graphContext.RootNode.IsInitialized == true)
                {
                    childGraph.DescribeState(output, indent + "  ");
                }
            }
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
            }
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

        private readonly Skeleton skeleton;

        // Created on first sample, since a graph tree can reference a thousand clips and plays a few
        private AnimationFrameCache? frameCache;
        private AnimationFrameCache FrameCache => frameCache ??= new AnimationFrameCache(skeleton, []);

        public GraphClip(ClipAnimation animation, Skeleton skeleton)
        {
            Animation = animation;
            this.skeleton = skeleton;

            var clipData = animation.Clip.Data.Root;
            var syncTrackData = clipData.GetProperty<KVObject>("m_syncTrack");
            SyncTrack = syncTrackData != null ? new AnimLib.SyncTrack(syncTrackData) : AnimLib.SyncTrack.Default;
            RootMotion = new AnimLib.RootMotionData(clipData.GetProperty<KVObject>("m_rootMotion") ?? new KVObject());

            foreach (var clipEvent in animation.Events)
            {
                if (clipEvent is NmIDEvent idEvent)
                {
                    StringToken.Store(idEvent.ID);
                }
                else if (clipEvent.ClassName == "CNmFloatCurveEvent")
                {
                    FloatCurves[clipEvent] = new AnimLib.FloatCurveEvent(clipEvent.Data).Curve;
                }
            }
        }

        /// <summary>The parsed curves of the clip's float curve events.</summary>
        public Dictionary<NmClipEvent, ValveResourceFormat.Particles.Utils.PiecewiseCurve> FloatCurves { get; } = [];

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
            var frame = FrameCache.GetFrame(Animation, frameIndex);
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
                var lastFrame = FrameCache.GetFrame(Animation, Animation.FrameCount - 1);
                CopyFrame(lastFrame, pose);
                return lastFrame;
            }

            var frame = FrameCache.GetInterpolatedFrame(Animation, time);
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
