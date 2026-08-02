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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            // Coefficients for noise scaling (noise returns -1..1)
            var valueScale = (outputMax - outputMin) * 0.5f;
            var valueBase = valueScale + outputMin;

            var timeOffset = noiseAnimationTimeScale * particleSystemState.Age;

            foreach (ref var particle in particles.Current)
            {
                // The engine scrolls the field along X only, and walks the second and third
                // channels cumulatively away from the first to decorrelate them
                var coordinate = (particle.Position * noiseScale) + new Vector3(timeOffset, 0f, 0f);
                var secondChannel = coordinate + new Vector3(100000.5f, 300000.25f, 9000001f);
                var thirdChannel = secondChannel + new Vector3(110000.25f, 310000.75f, 9100000f);

                var noise = new Vector3(
                    Utils.Noise.Value3D(coordinate),
                    Utils.Noise.Value3D(secondChannel),
                    Utils.Noise.Value3D(thirdChannel));

                var value = (noise * valueScale) + valueBase;

                if (!additive)
                {
                    particle.SetVector(outputField, value);
                    continue;
                }

                value *= frameTime * strength;
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
