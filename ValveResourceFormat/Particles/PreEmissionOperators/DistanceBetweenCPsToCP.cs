namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Measures the distance between two control points and remaps it into an output range,
    /// writing the result to a field of a third control point. <c>m_nSetParent</c> writes it on the
    /// parent or root system instead, and <c>m_bSetOnce</c> writes it only once until the system restarts.
    /// </summary>
    /// <remarks>
    /// With <c>m_bLOS</c> a trace runs from the start point toward the end one, and a blocked trace
    /// scales the distance by the hit fraction times <c>m_flLOSScale</c>, so with the default scale
    /// of 0 any hit makes the distance 0. A distance longer than <c>m_flMaxTraceLength</c> shortens
    /// the trace to that length along the direction of the end position from the world origin, not
    /// from the start point. <c>m_CollisionGroupName</c> and <c>m_nTraceSet</c> are not read; the
    /// trace hits all world collision.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DistanceBetweenCPsToCP">C_OP_DistanceBetweenCPsToCP</seealso>
    class DistanceBetweenCPsToCP : ParticleFunctionPreEmissionOperator
    {
        private readonly float distanceMin;
        private readonly float distanceMax = 128;
        private readonly float outputMin;
        private readonly float outputMax = 1;

        private readonly int startCP;
        private readonly int endCP = 1;
        private readonly int outputCP = 2;
        private readonly int outputCPField;
        private readonly bool setOnce;
        private readonly ParticleParentSetMode setParent = ParticleParentSetMode.PARTICLE_SET_PARENT_NO;
        private readonly bool los;
        private readonly float maxTraceLength = -1f;
        private readonly float losScale;

        private bool hasSet;

        public DistanceBetweenCPsToCP(ParticleDefinitionParser parse) : base(parse)
        {
            distanceMin = parse.Float("m_flInputMin", distanceMin);
            distanceMax = parse.Float("m_flInputMax", distanceMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            startCP = Math.Clamp(parse.Int32("m_nStartCP", startCP), 0, ParticleSystemState.MaxControlPoint);
            endCP = Math.Clamp(parse.Int32("m_nEndCP", endCP), 0, ParticleSystemState.MaxControlPoint);
            outputCP = Math.Clamp(parse.Int32("m_nOutputCP", outputCP), 0, ParticleSystemState.MaxControlPoint);
            outputCPField = Math.Clamp(parse.Int32("m_nOutputCPField", outputCPField), 0, 2);
            setOnce = parse.Boolean("m_bSetOnce", setOnce);
            setParent = parse.Enum("m_nSetParent", setParent);
            los = parse.Boolean("m_bLOS", los);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            losScale = parse.Float("m_flLOSScale", losScale);
        }

        public override void Reset()
        {
            hasSet = false;
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (setOnce)
            {
                if (hasSet)
                {
                    return;
                }

                hasSet = true;
            }

            var start = particleSystemState.GetControlPoint(startCP).Position;
            var end = particleSystemState.GetControlPoint(endCP).Position;

            var distance = Vector3.Distance(start, end);

            if (los)
            {
                var traceEnd = maxTraceLength != -1f && distance > maxTraceLength
                    ? start + (MathUtils.SafeNormalize(end) * maxTraceLength)
                    : end;

                particleSystemState.Collision.TraceRay(start, traceEnd, out _, out _, out var fraction);

                if (fraction != 1f)
                {
                    distance *= fraction * losScale;
                }
            }

            var finalValue = MathUtils.RemapValClamped(distance, distanceMin, distanceMax, outputMin, outputMax);

            particleSystemState.GetSetParentTarget(setParent).SetControlPointValueComponent(outputCP, outputCPField, finalValue);
        }
    }
}
