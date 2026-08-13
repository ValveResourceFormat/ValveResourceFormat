using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Adds an initial velocity from noise sampled at the particle's spatial creation coordinate plus
    /// a time term, mapping the noise output into a configurable minimum/maximum velocity range.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitialVelocityNoise">C_INIT_InitialVelocityNoise</seealso>
    class InitialVelocityNoise : ParticleFunctionInitializer
    {
        private readonly IVectorProvider outputMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider outputMax = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider noiseScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider noiseScaleLoc = new LiteralNumberProvider(0.01f);
        private readonly INumberProvider offset = new LiteralNumberProvider(0f);
        private readonly IVectorProvider offsetLoc = new LiteralVectorProvider(Vector3.Zero);
        private readonly Vector3 absVal;
        private readonly Vector3 absValInv;
        private readonly bool ignoreDt;
        private readonly ITransformProvider transformInput;

        public InitialVelocityNoise(ParticleDefinitionParser parse) : base(parse)
        {
            outputMin = parse.VectorProvider("m_vecOutputMin", outputMin);
            outputMax = parse.VectorProvider("m_vecOutputMax", outputMax);
            noiseScale = parse.NumberProvider("m_flNoiseScale", noiseScale);
            noiseScaleLoc = parse.NumberProvider("m_flNoiseScaleLoc", noiseScaleLoc);
            offset = parse.NumberProvider("m_flOffset", offset);
            offsetLoc = parse.VectorProvider("m_vecOffsetLoc", offsetLoc);
            absVal = parse.Vector3("m_vecAbsVal", absVal);
            absValInv = parse.Vector3("m_vecAbsValInv", absValInv);
            ignoreDt = parse.Boolean("m_bIgnoreDt", ignoreDt);
            transformInput = parse.TransformInput("m_TransformInput", new IdentityTransformProvider());
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var noiseScale = this.noiseScale.NextNumber(ref particle, particleSystemState);
            var noiseScaleLoc = this.noiseScaleLoc.NextNumber(ref particle, particleSystemState);
            var offsetLoc = this.offsetLoc.NextVector(ref particle, particleSystemState);
            var offset = this.offset.NextNumber(ref particle, particleSystemState);

            // The noise coordinate is spatial (particle creation position) plus a time term, so
            // particles emitted the same tick at different positions get decorrelated velocities.
            var samplePosition = ((particle.Position + offsetLoc) * noiseScaleLoc)
                + new Vector3((particle.CreationTime + offset) * noiseScale);
            var noise = new Vector3(
                Noise.Value3D(samplePosition),
                Noise.Value3D(samplePosition + new Vector3(100000.5f, 300000.25f, 9000001f)),
                Noise.Value3D(samplePosition + new Vector3(110000.25f, 310000.75f, 9100000f)));

            var min = outputMin.NextVector(ref particle, particleSystemState);
            var max = outputMax.NextVector(ref particle, particleSystemState);

            var anyInverted = absValInv != Vector3.Zero;
            var velocity = new Vector3(
                MapComponent(noise.X, absVal.X != 0f, absValInv.X != 0f, anyInverted, min.X, max.X),
                MapComponent(noise.Y, absVal.Y != 0f, absValInv.Y != 0f, anyInverted, min.Y, max.Y),
                MapComponent(noise.Z, absVal.Z != 0f, absValInv.Z != 0f, anyInverted, min.Z, max.Z));

            var transform = transformInput.NextTransform(ref particle, particleSystemState);
            var transformed = Vector3.TransformNormal(velocity, transform);

            // The engine skips the timestep multiply on its previous-position subtraction, which in
            // velocity terms divides by the spawn frame's timestep
            if (ignoreDt && particles.CurrentFrameTime > 0f)
            {
                transformed /= particles.CurrentFrameTime;
            }

            particle.Velocity += transformed;

            return particle;
        }

        /// <summary>
        /// When any component is inverted, inverted components become 1 + noise while the other
        /// components are sign-flipped, exceeding the usual output band; engine behavior.
        /// </summary>
        private static float MapComponent(float noise, bool absolute, bool inverted, bool anyInverted, float min, float max)
        {
            var value = absolute ? MathF.Abs(noise) : noise;

            if (anyInverted)
            {
                value = inverted ? 1f + value : -value;
            }

            var absScale = absolute ? 1f : 0.5f;
            var span = max - min;

            return (value * span * absScale) + min + (span * (1f - absScale));
        }
    }
}
