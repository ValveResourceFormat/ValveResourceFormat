namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// The line of sight test of the distance functions (<c>m_bLOS</c>, <c>m_flMaxTraceLength</c>,
    /// <c>m_flLOSScale</c>).
    /// </summary>
    static class LineOfSightTrace
    {
        /// <summary>
        /// Traces from <paramref name="start"/> toward <paramref name="end"/>, and scales
        /// <paramref name="distance"/> by the hit fraction times <paramref name="scale"/> when the trace is
        /// blocked. A distance longer than a <paramref name="maxTraceLength"/> other than -1 shortens the
        /// trace to that length along the direction of <paramref name="end"/> from the world origin.
        /// </summary>
        public static float ScaleDistance(IParticleCollision collision, Vector3 start, Vector3 end, float distance, float maxTraceLength, float scale)
        {
            var traceEnd = maxTraceLength != -1f && distance > maxTraceLength
                ? start + (MathUtils.SafeNormalize(end) * maxTraceLength)
                : end;

            collision.TraceRay(start, traceEnd, out _, out _, out var fraction);

            return fraction != 1f ? distance * (fraction * scale) : distance;
        }
    }
}
