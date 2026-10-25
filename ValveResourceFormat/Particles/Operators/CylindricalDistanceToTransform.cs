using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's perpendicular distance from the axis running between two transform
    /// origins into a scalar output field. The projection parameter is 0 at the end transform and
    /// 1 at the start; particles projecting outside that span have their distance forced to the
    /// outer radius plus one, which saturates the remap and fails the active-range test. When the
    /// two radii are equal the inner one is nudged down by one epsilon, turning the remap into a
    /// step instead of engaging the flat divisor guard.
    /// </summary>
    /// <remarks>
    /// Particles are processed in index-aligned blocks of four: with the active-range test on, a
    /// block where every lane fails is skipped without any writes, while a failed lane in a partially
    /// passing block is written with zero strength, which on the additive path still doubles the old
    /// value.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CylindricalDistanceToTransform">C_OP_CylindricalDistanceToTransform</seealso>
    class CylindricalDistanceToTransform : ParticleFunctionOperator
    {
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

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var start = transformStart.NextTransform(particleSystemState).Translation;
            var end = transformEnd.NextTransform(particleSystemState).Translation;
            var axis = start - end;
            var axisLengthSquared = Vector3.Dot(axis, axis);
            var axisLength = MathF.Sqrt(axisLengthSquared);
            var current = particles.Current;
            var clampOutput = outputField.IsAlpha();

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var inners = innerRadius.Fill(current, particleSystemState);
            var outers = outerRadius.Fill(current, particleSystemState);
            var outputMins = outputMin.Fill(current, particleSystemState);
            var outputMaxs = outputMax.Fill(current, particleSystemState);

            var positions = current.ReadVectors(ParticleField.Position);
            var outputs = current.Floats(outputField);
            var initials = particles.Initial.ReadFloats(outputField);

            Span<float> values = stackalloc float[4];
            Span<bool> pass = stackalloc bool[4];

            for (var offset = 0; offset < current.Length; offset += 4)
            {
                var anyPass = false;
                var blockLength = Math.Min(4, current.Length - offset);

                for (var lane = 0; lane < blockLength; lane++)
                {
                    var index = offset + lane;
                    var position = positions[index];

                    var inner = inners[index];
                    var outer = outers[index];
                    var minimumOutput = outputMins[index];
                    var maximumOutput = outputMaxs[index];

                    var t = axisLengthSquared < 1e-5f ? 0f : Vector3.Dot(axis, position - end) / axisLengthSquared;
                    var closest = end + axis * t;
                    var distance = Vector3.Distance(position, closest);

                    if (outer == inner)
                    {
                        inner -= ParticleMath.FloatEpsilon;
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
                        minimumOutput = MathUtils.Saturate(minimumOutput);
                        maximumOutput = MathUtils.Saturate(maximumOutput);
                    }

                    pass[lane] = !activeRange || (distance <= outer && inner <= distance);
                    anyPass |= pass[lane];

                    var remapped = ParticleMath.ClampedFraction(distance, inner, outer);
                    values[lane] = MathUtils.Lerp(minimumOutput, maximumOutput, remapped);
                }

                if (!anyPass)
                {
                    continue;
                }

                for (var lane = 0; lane < blockLength; lane++)
                {
                    var index = offset + lane;
                    var laneStrength = pass[lane] ? strength : 0f;

                    outputs[index] = DistanceToTransform.BlendRemapped(values[lane], initials[index], outputs[index], particleSystemState.CurrentStep, setMethod,
                        laneStrength, additive);
                }
            }
        }
    }
}
