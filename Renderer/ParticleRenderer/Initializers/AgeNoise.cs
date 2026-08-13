using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Starts particles partly aged: the value lattice sampled at the particle's scaled creation
    /// position with a time term added to every component maps to a clamped fraction of the
    /// particle's lifetime. Decorrelates bursts that would otherwise animate in lockstep.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_AgeNoise">C_INIT_AgeNoise</seealso>
    class AgeNoise : ParticleFunctionInitializer
    {
        private readonly bool absVal;
        private readonly bool absValInv;
        private readonly float offset;
        private readonly float ageMin;
        private readonly float ageMax = 1f;
        private readonly float noiseScale = 1f;
        private readonly float noiseScaleLoc = 1f;
        private readonly Vector3 offsetLoc = Vector3.Zero;

        public AgeNoise(ParticleDefinitionParser parse) : base(parse)
        {
            absVal = parse.Boolean("m_bAbsVal", absVal);
            absValInv = parse.Boolean("m_bAbsValInv", absValInv);
            offset = parse.Float("m_flOffset", offset);
            ageMin = parse.Float("m_flAgeMin", ageMin);
            ageMax = parse.Float("m_flAgeMax", ageMax);
            noiseScale = parse.Float("m_flNoiseScale", noiseScale);
            noiseScaleLoc = parse.Float("m_flNoiseScaleLoc", noiseScaleLoc);
            offsetLoc = parse.Vector3("m_vecOffsetLoc", offsetLoc);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var samplePosition = (particle.Position + offsetLoc) * noiseScaleLoc;
            var sampleTime = (particle.CreationTime + offset) * noiseScale;
            var noise = Noise.Value3D(samplePosition + new Vector3(sampleTime));

            // abs folds the signed noise into the full range; otherwise the half-span scale centers
            // it. absValInv inverts either way. Matches C_INIT_CreationNoise.
            var absScale = absVal ? 1f : 0.5f;
            var normalized = absVal ? MathF.Abs(noise) : noise;

            if (absValInv)
            {
                normalized = 1f - normalized;
            }

            var span = ageMax - ageMin;
            var fraction = Math.Clamp(ageMin + ((1f - absScale) * span) + (absScale * span * normalized), 0f, 1f);
            var age = fraction * particle.Lifetime;

            particle.Age += age;
            particle.CreationTime -= age;

            return particle;
        }
    }
}
