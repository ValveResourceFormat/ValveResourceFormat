using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    /// <summary>What drives the pose of an <see cref="AnimationPlayer"/>.</summary>
    public enum AnimationAlgorithm
    {
        /// <summary>Nothing, the skeleton holds its bind pose.</summary>
        None,

        /// <summary>Animation clips and sequences, mixed by the player.</summary>
        Sequence,

        /// <summary>An animation graph (.vnmgraph) evaluated every update.</summary>
        AnimGraph2,
    }

    /// <summary>
    /// A pose source an <see cref="AnimationPlayer"/> evaluates every update in place of its clips, such as an
    /// <see cref="AnimationGraph"/>.
    /// </summary>
    public interface IAnimationGraph
    {
        /// <summary>Gets the kind of graph, reported as the player's <see cref="AnimationPlayer.Algorithm"/>.</summary>
        AnimationAlgorithm Algorithm { get; }

        /// <summary>
        /// Gets the resource name of the skeleton the poses are for, which the graph plays on through the
        /// controller's external skeleton player, or an empty string for the model's own skeleton.
        /// </summary>
        string SkeletonName { get; }

        /// <summary>Gets the skeleton the poses are for.</summary>
        Skeleton Skeleton { get; }

        /// <summary>
        /// Gets the root motion of the last update, in the character's local space: the new world transform is
        /// this delta concatenated onto the previous one.
        /// </summary>
        FrameBone RootMotionDelta { get; }

        /// <summary>
        /// Advances the graph by <paramref name="timeStep"/> seconds and returns the resulting parent space pose,
        /// owned by the graph and valid until the next update.
        /// </summary>
        /// <param name="timeStep">The time to advance, in seconds.</param>
        /// <param name="worldTransform">Where the root motion has moved the character so far.</param>
        FrameBone[] Update(float timeStep, FrameBone worldTransform);
    }
}
