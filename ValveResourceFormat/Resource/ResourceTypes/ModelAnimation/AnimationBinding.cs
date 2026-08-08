namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Binds one ANIM block's segments to one target skeleton.
    /// </summary>
    /// <remarks>Cached and shared, so immutable.</remarks>
    internal sealed class AnimationBinding
    {
        /// <summary>
        /// Gets the skeleton this binding resolves onto. Decoding with a frame shaped for any other
        /// skeleton is invalid.
        /// </summary>
        public Skeleton Skeleton { get; }

        private readonly ElementRemap[][] segmentRemaps;

        internal AnimationBinding(Skeleton skeleton, ElementRemap[][] segmentRemaps)
        {
            Skeleton = skeleton;
            this.segmentRemaps = segmentRemaps;
        }

        /// <summary>
        /// Gets the elements the given segment writes on this skeleton.
        /// </summary>
        internal ReadOnlySpan<ElementRemap> GetRemaps(int segmentIndex) => segmentRemaps[segmentIndex];
    }
}
