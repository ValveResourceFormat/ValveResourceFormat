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
    /// <see cref="Min"/> instead of clamping. <see cref="Clamp"/> does not special-case it, <see cref="Normalize"/> does.</param>
    /// <param name="Loop">The period a looping parameter wraps by. A parameter without one clamps.</param>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CSeqPoseParamDesc">CSeqPoseParamDesc</seealso>
    public readonly record struct PoseParameter(string Name, float Min, float Max, bool Looping, float Loop = 0f)
    {
        /// <summary>
        /// Returns <paramref name="value"/> clamped to <see cref="Min"/>/<see cref="Max"/>.
        /// </summary>
        public float Clamp(float value) => MathUtils.Clamp(value, Min, Max);

        /// <summary>
        /// Returns <paramref name="value"/> as the parameter takes it: wrapped by the period when
        /// <see cref="Looping"/>, then clamped to <see cref="Min"/>/<see cref="Max"/>.
        /// </summary>
        public float Normalize(float value)
        {
            if (!Looping || Loop <= 0f)
            {
                return Clamp(value);
            }

            var wrapPoint = ((Min + Max) / 2f) + (Loop / 2f);

            return Clamp(MathUtils.Wrap(value, wrapPoint - Loop, wrapPoint));
        }
    }
}
