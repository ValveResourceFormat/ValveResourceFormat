using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Animation graph playback: when a graph is attached it replaces clip mixing as the
    /// player's pose source.
    /// </summary>
    public partial class AnimationPlayer
    {
        /// <summary>Gets the animation graph driving this player, or <see langword="null"/> when clips drive it.</summary>
        public IAnimationGraph? Graph { get; private set; }

        /// <summary>Gets what drives the pose: the attached graph, the clips, or nothing.</summary>
        public AnimationAlgorithm Algorithm => Graph?.Algorithm ?? (ActiveAnimation != null ? AnimationAlgorithm.Sequence : AnimationAlgorithm.None);

        /// <summary>
        /// Attaches an animation graph as this player's pose source, replacing any playing clips.
        /// Pass <see langword="null"/> to detach and return to clip playback.
        /// </summary>
        public void SetGraph(IAnimationGraph? graph)
        {
            Graph = graph;
            graphRootTransform = FrameBone.Identity;

            if (graph != null)
            {
                ClearClips();
            }

            forceUpdate = true;
        }

        private float pendingGraphStep;

        internal void StepGraph(float timeStep)
        {
            pendingGraphStep += timeStep;
            forceUpdate = true;
        }

        private bool UpdateFromGraph(IAnimationGraph graph, float timeStep, Matrix4x4 rootTransform)
        {
            if (IsPaused && !forceUpdate)
            {
                return false;
            }

            var graphPose = graph.Update(IsPaused ? pendingGraphStep : timeStep, graphRootTransform);
            pendingGraphStep = 0f;
            forceUpdate = false;

            AccumulateGraphRootMotion(graph.RootMotionDelta);

            // The graph does not sample flex data.
            AnimationFrame = null;

            foreach (var root in Skeleton.Roots)
            {
                ComputeWorldSubtree(root, rootTransform, graphPose, Pose);
            }

            return true;
        }

        // Where the graph's root motion has moved the character, relative to where the graph started
        private FrameBone graphRootTransform = FrameBone.Identity;

        /// <summary>
        /// Graph deltas are local to the character (new transform = delta * old), while
        /// <see cref="RootMotionDelta"/> composes each step after the motion so far, so the step is
        /// re-expressed relative to the accumulated transform.
        /// </summary>
        private void AccumulateGraphRootMotion(FrameBone delta)
        {
            if (delta == FrameBone.Identity)
            {
                return;
            }

            var next = delta * graphRootTransform;
            RootMotionDelta *= (graphRootTransform.Inverse() * next).ToMatrix();
            graphRootTransform = next;
        }

        private static void ComputeWorldSubtree(Bone bone, Matrix4x4 parentWorld, ReadOnlySpan<FrameBone> parentSpacePose, Span<Matrix4x4> world)
        {
            world[bone.Index] = parentSpacePose[bone.Index].ToMatrix() * parentWorld;

            foreach (var child in bone.Children)
            {
                ComputeWorldSubtree(child, world[bone.Index], parentSpacePose, world);
            }
        }
    }
}
