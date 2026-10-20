namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// The creation time window and the ease toward the remapped value that <see cref="RemapCPtoScalar"/>
    /// and <see cref="RemapCPtoVector"/> share.
    /// </summary>
    sealed class ControlPointRemapTiming
    {
        private readonly float startTime = -1f;
        private readonly float endTime = -1f;
        private readonly float interpRate;

        private bool interpolating;
        private Vector3 lastInput;
        private float interpolationStart;

        public ControlPointRemapTiming(ParticleDefinitionParser parse)
        {
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            interpRate = parse.Float("m_flInterpRate", interpRate);
        }

        public void Reset()
        {
            interpolating = false;
            interpolationStart = 0f;
        }

        /// <summary>
        /// Whether a particle created at <paramref name="creationTime"/> is remapped. A bound of -1 leaves
        /// the window open.
        /// </summary>
        public bool Covers(float creationTime)
            => startTime == -1f || endTime == -1f || (startTime <= creationTime && creationTime < endTime);

        /// <summary>
        /// How far the field eases toward the remapped value this step, 1 without a positive
        /// <c>m_flInterpRate</c>. The first step snaps to it. After that the clock restarts at 0 whenever
        /// the input changes or <c>m_flInterpRate</c> seconds have passed since the last restart, and
        /// climbs to 1 over that interval.
        /// </summary>
        public float NextInterpolation(Vector3 input, float time)
        {
            if (interpRate <= 0f)
            {
                return 1f;
            }

            if (!interpolating)
            {
                interpolating = true;
                lastInput = -input;
                return 1f;
            }

            var end = interpolationStart + interpRate;

            if (lastInput != input || time > end)
            {
                interpolationStart = time;
                lastInput = input;
            }

            return MathUtils.RemapValClamped(time, interpolationStart, end, 0f, 1f);
        }
    }
}
