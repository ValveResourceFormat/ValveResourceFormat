namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets a control point to a random position within a bounding box, optionally re-randomizing
    /// at a configurable rate and interpolating toward the new target position.
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

        private bool HasRunBefore;
        private float timeSinceLastRun;

        private Vector3 currentPosition = Vector3.Zero;

        private void GenerateNewPosition()
        {
            currentPosition = ParticleCollection.RandomBetweenPerComponent(Random.Shared.Next(), minPos, maxPos);
        }

        /// <summary>
        /// The current object-space position rotated and translated by the head control point.
        /// </summary>
        private Vector3 GetTargetPosition(ParticleSystemRenderState particleSystemState) => useWorldLocation
            ? currentPosition
            : ControlPointTransformProvider.TransformPosition(particleSystemState, offsetCP, currentPosition);

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var orientation = orient
                ? particleSystemState.GetControlPoint(offsetCP).Orientation
                : Vector3.Zero;

            // We need to start off with an initial value, regardless of interpolation
            if (!HasRunBefore)
            {
                GenerateNewPosition();

                particleSystemState.SetControlPointValue(cp, GetTargetPosition(particleSystemState));

                if (orient)
                {
                    particleSystemState.SetControlPointOrientation(cp, orientation);
                }

                HasRunBefore = true;
            }

            timeSinceLastRun += frameTime;

            var reRandomRate = this.reRandomRate.NextNumber(particleSystemState);
            if (reRandomRate > 0f)
            {
                var lerpOld = particleSystemState.GetControlPoint(cp).Position;
                var lerpNew = GetTargetPosition(particleSystemState);

                // exponential fade like all the other lerps
                var positionBlended = Vector3.Lerp(lerpOld, lerpNew, interpolation.NextNumber(particleSystemState));

                // orientation doesn't lerp in the same way that position does

                particleSystemState.SetControlPointValue(cp, positionBlended);

                if (orient)
                {
                    particleSystemState.SetControlPointOrientation(cp, orientation);
                }

                // If we need to generate a new position
                if (timeSinceLastRun > reRandomRate)
                {
                    GenerateNewPosition();

                    // Subtract reRandomRate instead of resetting to 0 so we can maintain sub-frame timing
                    timeSinceLastRun -= reRandomRate;
                }
            }
        }
    }
}
