using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets a scalar attribute at spawn from the particle's distance to a control point, remapped
    /// between input and output ranges with optional Schlick bias, per-component distance scaling,
    /// and an active range that skips particles outside it. Line-of-sight testing is not supported.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_DistanceToCPInit">C_INIT_DistanceToCPInit</seealso>
    class DistanceToCPInit : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(128f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);
        private readonly int startCP;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;
        private readonly Vector3 distanceScale = Vector3.One;
        private readonly float remapBias = 0.5f;
        private readonly bool hasBias;

        public DistanceToCPInit(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            startCP = Math.Clamp(parse.Int32("m_nStartCP", startCP), 0, 63);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            distanceScale = parse.Vector3("m_vecDistanceScale", distanceScale);
            remapBias = parse.Float("m_flRemapBias", remapBias);
            hasBias = remapBias != 0.5f;
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var controlPointPosition = particleSystemState.GetControlPoint(startCP).Position;

            var inMin = inputMin.NextNumber(ref particle, particleSystemState);
            var inMax = inputMax.NextNumber(ref particle, particleSystemState);
            var outMin = outputMin.NextNumber(ref particle, particleSystemState);
            var outMax = outputMax.NextNumber(ref particle, particleSystemState);

            if (fieldOutput is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outMin = Math.Clamp(outMin, 0f, 1f);
                outMax = Math.Clamp(outMax, 0f, 1f);
            }

            var distance = ((controlPointPosition - particle.Position) * distanceScale).Length();

            if (activeRange && (distance < inMin || distance > inMax))
            {
                return particle;
            }

            var value = inMin == inMax
                ? (distance < inMax ? outMin : outMax)
                : outMin + MathUtils.Saturate((distance - inMin) / (inMax - inMin)) * (outMax - outMin);

            if (hasBias)
            {
                value = NumericBias.Standard(Math.Clamp(value, 0f, 1f), remapBias);
            }

            value = particle.ModifyScalarBySetMethodAtSpawn(particles, fieldOutput, value, setMethod);
            particle.SetScalar(fieldOutput, value);

            return particle;
        }
    }
}
