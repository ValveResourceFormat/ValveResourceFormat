namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's perpendicular distance from the axis running between two transform
    /// origins into a scalar output field. The projection parameter is 0 at the end transform and
    /// 1 at the start; particles projecting outside that span have their distance forced to the
    /// outer radius plus one, which saturates the remap and fails the active-range test. When the
    /// two radii are equal the inner one is nudged down by one epsilon, turning the remap into a
    /// step instead of engaging the flat divisor guard.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CylindricalDistanceToTransform">C_OP_CylindricalDistanceToTransform</seealso>
    class CylindricalDistanceToTransform : ParticleFunctionOperator
    {
        private const float FltEpsilon = 1.1920929e-7f;

        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider innerRadius = new LiteralNumberProvider(0f);
        private readonly INumberProvider outerRadius = new LiteralNumberProvider(64f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly ITransformProvider transformStart = new ControlPointTransformProvider();
        private readonly ITransformProvider transformEnd = new ControlPointTransformProvider();
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly bool additive;
        private readonly bool capsule;

        public CylindricalDistanceToTransform(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            innerRadius = parse.NumberProvider("m_flInputMin", innerRadius);
            outerRadius = parse.NumberProvider("m_flInputMax", outerRadius);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            transformStart = parse.TransformInput("m_TransformStart", transformStart);
            transformEnd = parse.TransformInput("m_TransformEnd", transformEnd);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            additive = parse.Boolean("m_bAdditive", additive);
            capsule = parse.Boolean("m_bCapsule", capsule);
        }

        /// <summary>
        /// Particles are processed in index-aligned groups of four, mirroring the engine's SIMD
        /// blocks: with the active-range test on, a group where every lane fails is skipped
        /// without any writes, while a failed lane in a partially passing group is written with
        /// zero strength - which on the additive path still doubles the old value.
        /// </summary>
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var start = transformStart.NextTransform(particleSystemState).Translation;
            var end = transformEnd.NextTransform(particleSystemState).Translation;
            var axis = start - end;
            var axisLengthSquared = Vector3.Dot(axis, axis);
            var axisLength = MathF.Sqrt(axisLengthSquared);
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

                    var inner = innerRadius.NextNumber(ref particle, particleSystemState);
                    var outer = outerRadius.NextNumber(ref particle, particleSystemState);
                    var outputMin = this.outputMin.NextNumber(ref particle, particleSystemState);
                    var outputMax = this.outputMax.NextNumber(ref particle, particleSystemState);

                    var t = axisLengthSquared < 1e-5f ? 0f : Vector3.Dot(axis, particle.Position - end) / axisLengthSquared;
                    var closest = end + axis * t;
                    var distance = Vector3.Distance(particle.Position, closest);

                    if (outer == inner)
                    {
                        inner -= FltEpsilon;
                    }

                    if (capsule)
                    {
                        var axial = t * axisLength;

                        if (axial <= outer)
                        {
                            distance = MathF.Max(outer - axial, distance);
                        }

                        if (axisLength - outer <= axial)
                        {
                            distance = MathF.Max(outer - (axisLength - axial), distance);
                        }
                    }

                    if (t > 1f || t < 0f)
                    {
                        distance = outer + 1f;
                    }

                    if (clampOutput)
                    {
                        outputMin = MathUtils.Saturate(outputMin);
                        outputMax = MathUtils.Saturate(outputMax);
                    }

                    pass[lane] = !activeRange || (distance <= outer && inner <= distance);
                    anyPass |= pass[lane];

                    var divisor = outer == inner ? 1f : outer - inner;
                    var remapped = MathUtils.Saturate((distance - inner) / divisor);
                    values[lane] = float.Lerp(outputMin, outputMax, remapped);
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
