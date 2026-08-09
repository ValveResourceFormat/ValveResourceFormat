namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets a control point to a random position within a bounding box, re-randomizing at a configurable
    /// rate and interpolating toward the new target position every frame. A first target is drawn when the
    /// system starts, and a negative <c>m_flReRandomRate</c> keeps that target for the system's lifetime.
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
            cp = parse.Int32("m_nCP1", cp);
            minPos = parse.Vector3("m_vecCPMinPos", minPos);
            maxPos = parse.Vector3("m_vecCPMaxPos", maxPos);
            useWorldLocation = parse.Boolean("m_bUseWorldLocation", useWorldLocation);
            offsetCP = parse.Int32("m_nHeadLocation", offsetCP);
            orient = parse.Boolean("m_bOrient", orient);
            reRandomRate = parse.NumberProvider("m_flReRandomRate", reRandomRate);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
        }

        private bool initialised;
        private float lastDrawTime;

        private Vector3 currentPosition = Vector3.Zero;

        /// <summary>
        /// The current position, rotated and translated by the head control point, unless world location is used, in which case the position is returned as-is.
        /// </summary>
        private Vector3 GetTargetPosition(ParticleSystemRenderState particleSystemState) => useWorldLocation
            ? currentPosition
            : ControlPointTransformProvider.TransformPosition(particleSystemState, offsetCP, currentPosition);

        public override void Reset()
        {
            initialised = false;
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (!initialised)
            {
                initialised = true;
                lastDrawTime = float.MinValue;
                currentPosition = particleSystemState.Random.NextBetweenPerComponent(minPos, maxPos);
            }

            // Both inputs are sampled before the gate so their draws stay in step whether or not it fires
            var lerpAmount = interpolation.NextNumber(particleSystemState);
            var rate = reRandomRate.NextNumber(particleSystemState);

            var firstRun = lastDrawTime == float.MinValue;

            if (firstRun)
            {
                lerpAmount = 1f;
            }

            if (particleSystemState.Age >= lastDrawTime + rate)
            {
                currentPosition = particleSystemState.Random.NextBetweenPerComponent(minPos, maxPos);
                lastDrawTime = rate < 0f ? float.MaxValue : particleSystemState.Age;
            }

            var blendedPosition = Vector3.Lerp(
                particleSystemState.GetControlPoint(cp).Position,
                GetTargetPosition(particleSystemState),
                lerpAmount);

            particleSystemState.SetControlPointValue(cp, blendedPosition);

            if (orient)
            {
                particleSystemState.SetControlPointOrientation(cp, particleSystemState.GetControlPoint(offsetCP).Orientation);
            }
        }
    }
}
