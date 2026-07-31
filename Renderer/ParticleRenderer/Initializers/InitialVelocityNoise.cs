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
        private readonly ITransformProvider transformInput;

        public InitialVelocityNoise(ParticleDefinitionParser parse) : base(parse)
        {
            outputMin = parse.VectorProvider("m_vecOutputMin", outputMin);
            outputMax = parse.VectorProvider("m_vecOutputMax", outputMax);
            noiseScale = parse.NumberProvider("m_flNoiseScale", noiseScale);
            noiseScaleLoc = parse.NumberProvider("m_flNoiseScaleLoc", noiseScaleLoc);
            offset = parse.NumberProvider("m_flOffset", offset);
            offsetLoc = parse.VectorProvider("m_vecOffsetLoc", offsetLoc);
            transformInput = parse.TransformInput("m_TransformInput", new ControlPointTransformProvider(0, true));
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var noiseScale = this.noiseScale.NextNumber(ref particle, particleSystemState);
            var noiseScaleLoc = this.noiseScaleLoc.NextNumber(ref particle, particleSystemState);
            var offsetLoc = this.offsetLoc.NextVector(ref particle, particleSystemState);
            var offset = this.offset.NextNumber(ref particle, particleSystemState);

            // The noise coordinate is spatial (particle creation position) plus a time term, so
            // particles emitted the same tick at different positions get decorrelated velocities.
            var samplePosition = (particle.Position + offsetLoc) * noiseScaleLoc;
            var coordinate = (samplePosition.X * 0.7f) + (samplePosition.Y * 1.3f) + (samplePosition.Z * 2.1f)
                + ((particleSystemState.Age + offset) * noiseScale);
            var r = new Vector3(
                Noise.Simplex1D(coordinate),
                Noise.Simplex1D(coordinate + 101723),
                Noise.Simplex1D(coordinate + 555557));

            var min = outputMin.NextVector(ref particle, particleSystemState);
            var max = outputMax.NextVector(ref particle, particleSystemState);

            // The velocity is authored in the transform input's frame (default control point 0), so a
            // rotated emitter (e.g. a pitched map entity) rotates it into world space.
            var transform = transformInput.NextTransform(ref particle, particleSystemState);
            particle.Velocity += Vector3.TransformNormal(Vector3.Lerp(min, max, (r * 0.5f) + new Vector3(0.5f)), transform);

            return particle;
        }
    }
}
