using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
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

        public override ulong WrittenFields => ParticleField.CreationTime.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var positions = run.ReadVectors(ParticleField.Position);
            var lifetimes = run.ReadFloats(ParticleField.LifeDuration);
            var creationTimes = run.Floats(ParticleField.CreationTime);

            for (var i = 0; i < run.Length; i++)
            {
                var samplePosition = (positions[i] + offsetLoc) * noiseScaleLoc;
                var sampleTime = (creationTimes[i] + offset) * noiseScale;
                var noise = Noise.Value3D(samplePosition + new Vector3(sampleTime));

                var fraction = ParticleMath.ClampToUnit(CreationNoise.MapToRange(noise, absVal, absValInv, ageMin, ageMax));
                var age = fraction * lifetimes[i];

                creationTimes[i] -= age;
            }
        }
    }
}
