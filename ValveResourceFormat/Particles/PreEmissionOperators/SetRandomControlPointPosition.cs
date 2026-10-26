namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets a control point to a random position within a bounding box, re-randomizing at a configurable
    /// rate and interpolating toward the new target position every frame. A first target is drawn and, in
    /// world space, written when the system is created; the first step draws again and snaps to the new
    /// target, and a negative <c>m_flReRandomRate</c> keeps that one for the system's lifetime, restarts
    /// included. <c>m_bOrient</c> copies the head control point's orientation outside world space only.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetRandomControlPointPosition">C_OP_SetRandomControlPointPosition</seealso>
    class SetRandomControlPointPosition : ParticleFunctionPreEmissionOperator
    {
        private readonly int cp = 1;
        private readonly Vector3 minPos = Vector3.Zero;
        private readonly Vector3 maxPos = Vector3.Zero;

        private readonly bool useWorldLocation;
        private readonly bool orient;
        private readonly int offsetCP;

        private readonly INumberProvider reRandomRate = new LiteralNumberProvider(-1.0f);
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1.0f);

        public SetRandomControlPointPosition(ParticleDefinitionParser parse) : base(parse)
        {
            cp = parse.ControlPoint("m_nCP1", cp);
            minPos = parse.Vector3("m_vecCPMinPos", minPos);
            maxPos = parse.Vector3("m_vecCPMaxPos", maxPos);
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            offsetCP = parse.Int32("m_nHeadLocation", offsetCP);
            orient = parse.Boolean("m_bOrient", orient);
            reRandomRate = parse.NumberProvider("m_flReRandomRate", reRandomRate);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
        }

        private float lastDrawTime;

        private Vector3 currentPosition = Vector3.Zero;

        /// <summary>
        /// Current position rotated and translated by the head control point, or as-is with world location.
        /// </summary>
        private Vector3 GetTargetPosition(ParticleSystemState particleSystemState) => useWorldLocation
            ? currentPosition
            : ControlPointTransformProvider.TransformPosition(particleSystemState, offsetCP, currentPosition);

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            lastDrawTime = float.MinValue;
            currentPosition = particleSystemState.Random.NextBetweenPerComponentSeededTwice(minPos, maxPos);

            if (useWorldLocation)
            {
                particleSystemState.SetControlPointValue(cp, currentPosition);
            }
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            // Both inputs are sampled before the gate so their draws stay in step whether or not it fires
            var lerpAmount = interpolation.NextNumber(particleSystemState);
            var rate = reRandomRate.NextNumber(particleSystemState);

            if (lastDrawTime == float.MinValue)
            {
                lerpAmount = 1f;
            }

            if (particleSystemState.Age >= lastDrawTime + rate)
            {
                currentPosition = particleSystemState.Random.NextBetweenPerComponentSeededTwice(minPos, maxPos);
                lastDrawTime = rate < 0f ? float.MaxValue : particleSystemState.Age;
            }

            var blendedPosition = Vector3.Lerp(
                particleSystemState.GetControlPoint(cp).Position,
                GetTargetPosition(particleSystemState),
                lerpAmount);

            particleSystemState.SetControlPointValue(cp, blendedPosition);

            if (!useWorldLocation && orient)
            {
                var head = particleSystemState.GetControlPoint(offsetCP);
                particleSystemState.SetControlPointOrientation(cp, head.Forward, head.Left, head.Up);
            }
        }
    }
}
