using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes a scalar particle attribute from spatial noise sampled at the particle's creation
    /// position, remapped into an output range. Sparse convolution noise is approximated here
    /// with simplex noise over a linear position hash.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreationNoise">C_INIT_CreationNoise</seealso>
    class CreationNoise : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly bool absVal;
        private readonly bool absValInv;
        private readonly float offset;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly float noiseScale = 0.1f;
        private readonly float noiseScaleLoc = 0.001f;
        private readonly Vector3 offsetLoc = Vector3.Zero;

        public CreationNoise(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            absVal = parse.Boolean("m_bAbsVal", absVal);
            absValInv = parse.Boolean("m_bAbsValInv", absValInv);
            offset = parse.Float("m_flOffset", offset);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            noiseScale = parse.Float("m_flNoiseScale", noiseScale);
            noiseScaleLoc = parse.Float("m_flNoiseScaleLoc", noiseScaleLoc);
            offsetLoc = parse.Vector3("m_vecOffsetLoc", offsetLoc);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            if (fieldOutput.FieldType() != "float")
            {
                return particle;
            }

            var samplePosition = (particle.Position + offsetLoc) * noiseScaleLoc;
            var sampleTime = (particleSystemState.Age + offset) * noiseScale;
            var noise = Noise.Simplex1D((samplePosition.X * 0.7f) + (samplePosition.Y * 1.3f) + (samplePosition.Z * 2.1f) + sampleTime);

            var normalized = absVal || absValInv
                ? MathF.Abs(noise)
                : (noise * 0.5f) + 0.5f;

            if (absValInv)
            {
                normalized = 1f - normalized;
            }

            particle.SetScalar(fieldOutput, float.Lerp(outputMin, outputMax, normalized));

            return particle;
        }
    }
}
