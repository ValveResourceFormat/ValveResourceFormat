namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's component-scaled distance from a transform's origin into a scalar
    /// output field. The remap runs entirely in squared-distance space - the input range ends are
    /// squared and the distance is never square-rooted - so the response is quadratic in distance.
    /// </summary>
    /// <remarks>
    /// With <c>m_bLOS</c> a trace runs from the transform to each particle that passes the active
    /// range, and a blocked trace scales the squared distance by the hit fraction times
    /// <c>m_flLOSScale</c>, so with the default scale of 0 any hit makes it 0. When the squared
    /// distance exceeds <c>m_flMaxTraceLength</c> (compared unsquared), the trace is shortened to that
    /// length along the direction of the particle position from the world origin.
    /// <c>m_CollisionGroupName</c> and <c>m_nTraceSet</c> are not read; the trace hits all world
    /// collision.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DistanceToTransform">C_OP_DistanceToTransform</seealso>
    class DistanceToTransform : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(128f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly ITransformProvider transformStart = new ControlPointTransformProvider();
        private readonly bool los;
        private readonly float maxTraceLength = -1f;
        private readonly float losScale;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly bool additive;
        private readonly IVectorProvider componentScale = new LiteralVectorProvider(Vector3.One);

        public DistanceToTransform(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            transformStart = parse.TransformInput("m_TransformStart", transformStart);
            los = parse.Boolean("m_bLOS", los);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            losScale = parse.Float("m_flLOSScale", losScale);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            additive = parse.Boolean("m_bAdditive", additive);
            componentScale = parse.VectorProvider("m_vecComponentScale", componentScale);
        }

        /// <summary>
        /// Particles are processed in index-aligned groups of four, mirroring the engine's SIMD
        /// blocks: with the active-range test on, a group where every lane fails is skipped
        /// without any writes, while a failed lane in a partially passing group is written with
        /// zero strength - which on the additive path still doubles the old value.
        /// </summary>
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var origin = transformStart.NextTransform(particleSystemState).Translation;
            var particleSpan = particles.Current;
            var clampOutput = outputField is ParticleField.Alpha or ParticleField.AlphaAlternate;

            Span<float> values = stackalloc float[4];
            Span<bool> pass = stackalloc bool[4];

            for (var head = 0; head < particleSpan.Length; head += 4)
            {
                var lanes = Math.Min(4, particleSpan.Length - head);
                var anyPass = false;

                for (var lane = 0; lane < lanes; lane++)
                {
                    ref var particle = ref particleSpan[head + lane];

                    var inputMin = this.inputMin.NextNumber(ref particle, particleSystemState);
                    var inputMax = this.inputMax.NextNumber(ref particle, particleSystemState);
                    var outputMin = this.outputMin.NextNumber(ref particle, particleSystemState);
                    var outputMax = this.outputMax.NextNumber(ref particle, particleSystemState);
                    var scale = componentScale.NextVector(ref particle, particleSystemState);

                    var scaledDelta = (particle.Position - origin) * scale;
                    var distanceSquared = Vector3.Dot(scaledDelta, scaledDelta);
                    var inputMinSquared = inputMin * inputMin;
                    var inputMaxSquared = inputMax * inputMax;

                    pass[lane] = !activeRange || (distanceSquared <= inputMaxSquared && inputMinSquared <= distanceSquared);
                    anyPass |= pass[lane];

                    if (los && pass[lane])
                    {
                        distanceSquared = LineOfSightTrace.ScaleDistance(particleSystemState.Collision, origin, particle.Position, distanceSquared, maxTraceLength, losScale);
                    }

                    if (clampOutput)
                    {
                        outputMin = MathUtils.Saturate(outputMin);
                        outputMax = MathUtils.Saturate(outputMax);
                    }

                    var divisor = inputMaxSquared == inputMinSquared ? 1f : inputMaxSquared - inputMinSquared;
                    var remapped = MathUtils.Saturate((distanceSquared - inputMinSquared) / divisor);
                    values[lane] = MathUtils.Lerp(outputMin, outputMax, remapped);
                }

                if (!anyPass)
                {
                    continue;
                }

                for (var lane = 0; lane < lanes; lane++)
                {
                    ref var particle = ref particleSpan[head + lane];

                    var finalValue = particle.ModifyScalarBySetMethod(particles, outputField, values[lane], setMethod);
                    var current = particle.GetScalar(outputField);
                    var delta = (finalValue - current) * (pass[lane] ? strength : 0f);

                    particle.SetScalar(outputField, additive ? current + (current + delta) : current + delta);
                }
            }
        }
    }
}
