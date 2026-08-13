namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Remaps the distance between two transform origins into a scalar output field. Particle
    /// positions are never read; only the per-particle range inputs vary the result across
    /// particles.
    /// </summary>
    /// <remarks>
    /// The line-of-sight fields (<c>m_bLOS</c>, <c>m_CollisionGroupName</c>, <c>m_nTraceSet</c>,
    /// <c>m_flMaxTraceLength</c>, <c>m_flLOSScale</c>) gate an occlusion trace between the two
    /// transforms. VRF performs no collision traces, so every trace is clear (fraction 1.0) and
    /// no occlusion factor is ever applied.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DistanceBetweenTransforms">C_OP_DistanceBetweenTransforms</seealso>
    class DistanceBetweenTransforms : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(128f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly ITransformProvider transformStart = new ControlPointTransformProvider();
        private readonly ITransformProvider transformEnd = new ControlPointTransformProvider();
        private readonly bool los;
        private readonly float losScale;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public DistanceBetweenTransforms(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            transformStart = parse.TransformInput("m_TransformStart", transformStart);
            transformEnd = parse.TransformInput("m_TransformEnd", transformEnd);
            los = parse.Boolean("m_bLOS", los);
            losScale = parse.Float("m_flLOSScale", losScale);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var start = transformStart.NextTransform(particleSystemState).Translation;
            var end = transformEnd.NextTransform(particleSystemState).Translation;
            var distance = Vector3.Distance(start, end);

            if (los)
            {
                var fraction = TraceFraction();
                if (fraction != 1f)
                {
                    distance *= fraction * losScale;
                }
            }

            foreach (ref var particle in particles.Current)
            {
                var inputMin = this.inputMin.NextNumber(ref particle, particleSystemState);
                var inputMax = this.inputMax.NextNumber(ref particle, particleSystemState);
                var outputMin = this.outputMin.NextNumber(ref particle, particleSystemState);
                var outputMax = this.outputMax.NextNumber(ref particle, particleSystemState);

                var divisor = inputMax == inputMin ? 1f : inputMax - inputMin;
                var remapped = MathUtils.Saturate((distance - inputMin) / divisor);
                var finalValue = float.Lerp(outputMin, outputMax, remapped);

                finalValue = particle.ModifyScalarBySetMethod(particles, outputField, finalValue, setMethod);

                particle.SetScalar(outputField, float.Lerp(particle.GetScalar(outputField), finalValue, strength));
            }
        }

        /// <summary>
        /// Completed fraction of the occlusion trace. VRF has no collision tracing, so every trace
        /// runs clear and returns 1.0.
        /// </summary>
        private static float TraceFraction() => 1f;
    }
}
