namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Applies noise sampled at the particle's position to a vector attribute, mapping the noise
    /// into a configurable per-component output range. Can operate additively or by replacing the
    /// current value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_VectorNoise">C_OP_VectorNoise</seealso>
    class VectorNoise : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Color;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly float noiseScale = 1f;
        private readonly float noiseAnimationTimeScale;
        private readonly bool additive;

        public VectorNoise(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);
            noiseScale = parse.Float("m_fl4NoiseScale", noiseScale);
            noiseAnimationTimeScale = parse.Float("m_flNoiseAnimationTimeScale", noiseAnimationTimeScale);
            additive = parse.Boolean("m_bAdditive", additive);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // Coefficients for noise scaling (noise returns -1..1)
            var valueScale = (outputMax - outputMin) * 0.5f;
            var valueBase = valueScale + outputMin;

            var timeOffset = noiseAnimationTimeScale * particleSystemState.Age;

            foreach (ref var particle in particles.Current)
            {
                var coordinate = (particle.Position * noiseScale) + new Vector3(timeOffset);
                var noise = new Vector3(
                    Utils.Noise.Value3D(coordinate),
                    Utils.Noise.Value3D(coordinate + new Vector3(31.416f, 17.239f, 0f)),
                    Utils.Noise.Value3D(coordinate + new Vector3(0f, 47.853f, 63.271f)));

                var value = (noise * valueScale) + valueBase;

                if (!additive)
                {
                    particle.SetVector(outputField, value);
                    continue;
                }

                value *= frameTime;
                particle.SetVector(outputField, particle.GetVector(outputField) + value);

                // The previous position moves with the offset so it is not read back as velocity
                if (outputField == ParticleField.Position)
                {
                    particle.PositionPrevious += value;
                }
            }
        }
    }
}
