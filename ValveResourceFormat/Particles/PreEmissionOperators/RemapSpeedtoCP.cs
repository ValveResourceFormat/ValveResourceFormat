namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Remaps how fast a control point is moving into an output range and writes the result to a
    /// component of another control point. With <c>m_bUseDeltaV</c> the input is instead how much that
    /// control point's velocity changed since the previous frame, which reads as acceleration.
    ///
    /// <para>A degenerate input range (<c>m_flInputMin</c> equal to <c>m_flInputMax</c>) becomes a step:
    /// speeds below the threshold map to <c>m_flOutputMin</c>, everything else to <c>m_flOutputMax</c>.</para>
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapSpeedtoCP">C_OP_RemapSpeedtoCP</seealso>
    class RemapSpeedtoCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int inControlPoint;
        private readonly int outControlPoint = -1;
        private readonly int field;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly bool useDeltaV;

        private Vector3 previousVelocity;

        public RemapSpeedtoCP(ParticleDefinitionParser parse) : base(parse)
        {
            inControlPoint = parse.Int32("m_nInControlPointNumber", inControlPoint);
            outControlPoint = parse.Int32("m_nOutControlPointNumber", outControlPoint);
            field = parse.Int32("m_nField", field);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            useDeltaV = parse.Boolean("m_bUseDeltaV", useDeltaV);
        }

        public override void Reset()
        {
            previousVelocity = Vector3.Zero;
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime)
        {
            // The engine's control point writer takes the field index unsigned and rejects anything
            // above 3, and index 3 addresses a component the position vector does not have
            if (outControlPoint < 0 || field < 0 || field > 2 || frameTime <= 0f)
            {
                return;
            }

            var velocity = particleSystemState.GetControlPoint(inControlPoint).Velocity;

            float speed;

            if (useDeltaV)
            {
                speed = Vector3.Distance(velocity, previousVelocity);
                previousVelocity = velocity;
            }
            else
            {
                speed = velocity.Length();
            }

            var output = MathUtils.RemapValClamped(speed, inputMin, inputMax, outputMin, outputMax);

            particleSystemState.SetControlPointValueComponent(outControlPoint, field, output);
        }
    }
}
