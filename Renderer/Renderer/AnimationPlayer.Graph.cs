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
        public AnimationGraph? Graph { get; private set; }

        /// <summary>
        /// Attaches an animation graph as this player's pose source, replacing any playing clips.
        /// Pass <see langword="null"/> to detach and return to clip playback.
        /// </summary>
        public void SetGraph(AnimationGraph? graph)
        {
            Graph = graph;

            if (graph != null)
            {
                ClearClips();
            }

            forceUpdate = true;
        }

        private bool UpdateFromGraph(AnimationGraph graph, float timeStep, Matrix4x4 rootTransform)
        {
            if (IsPaused && !forceUpdate)
            {
                return false;
            }

            var graphPose = graph.Update(IsPaused ? 0f : timeStep);
            forceUpdate = false;

            // The graph does not sample flex data.
            AnimationFrame = null;

            foreach (var root in Skeleton.Roots)
            {
                ComputeWorldSubtree(root, rootTransform, graphPose, Pose);
            }

            return true;
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
