namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// A named pose parameter a blend sequence's <see cref="AnimationFetch.LocalPose"/> positions its
    /// animations along, one per dimension.
    /// </summary>
    /// <param name="Name">The parameter's name, matched against <see cref="AnimationFetch.LocalPose"/>
    /// through <see cref="SequenceAnimation.PoseParameterNames"/>.</param>
    /// <param name="Min">The value at which the parameter's range starts.</param>
    /// <param name="Max">The value at which the parameter's range ends.</param>
    /// <param name="Looping">Whether the parameter wraps from <see cref="Max"/> back to
    /// <see cref="Min"/> instead of clamping. <see cref="Clamp"/> does not special-case it.</param>
    public readonly record struct PoseParameter(string Name, float Min, float Max, bool Looping)
    {
        /// <summary>
        /// Returns <paramref name="value"/> clamped to <see cref="Min"/>/<see cref="Max"/>.
        /// </summary>
        public float Clamp(float value) => Math.Clamp(value, Min, Max);
    }
}
