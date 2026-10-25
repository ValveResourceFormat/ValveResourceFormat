using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Initializes a scalar particle attribute from the value lattice sampled at the particle's
    /// scaled creation position with a time term added to every component, remapped into an output
    /// range. The time term is taken once per call, from the first particle, and shared by the rest; in
    /// the whole blocks of a batch each lane takes it from the first block. Angle fields author the
    /// range in degrees; alpha fields clamp to [0, 1].
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
        private readonly float worldTimeScale;
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
            worldTimeScale = parse.Float("m_flWorldTimeScale", worldTimeScale);
            offsetLoc = parse.Vector3("m_vecOffsetLoc", offsetLoc);
        }

        public override ulong WrittenFields => fieldOutput.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);

            // The world time term ticks in milliseconds
            var timeTerm = ((creationTimes[0] + offset) * noiseScale)
                + (particleSystemState.WorldTime * 1000f * worldTimeScale);

            Span<float> laneTimeTerms = stackalloc float[4];
            laneTimeTerms.Fill(timeTerm);

            WriteNoise(run, laneTimeTerms);
        }

        /// <summary>
        /// Takes the time term of each lane from the creation time of the particle in that lane of the
        /// first block, for every block, and leaves the world time out.
        /// </summary>
        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);

            Span<float> laneTimeTerms = stackalloc float[4];

            for (var lane = 0; lane < laneTimeTerms.Length; lane++)
            {
                laneTimeTerms[lane] = (creationTimes[lane] + offset) * noiseScale;
            }

            WriteNoise(run, laneTimeTerms);
        }

        /// <summary>
        /// Writes the mapped noise at each particle's position, offset by the time term of its lane in
        /// <paramref name="laneTimeTerms"/>.
        /// </summary>
        private void WriteNoise(ParticleSpan run, ReadOnlySpan<float> laneTimeTerms)
        {
            var positions = run.ReadVectors(ParticleField.Position);
            var outputs = run.Floats(fieldOutput);

            var isAngle = fieldOutput.IsOrientationField();
            var min = isAngle ? float.DegreesToRadians(outputMin) : outputMin;
            var max = isAngle ? float.DegreesToRadians(outputMax) : outputMax;
            var isAlpha = fieldOutput.IsAlpha();

            for (var i = 0; i < run.Length; i++)
            {
                var samplePosition = (positions[i] + offsetLoc) * noiseScaleLoc;
                var noise = Noise.Value3D(samplePosition + new Vector3(laneTimeTerms[i & 3]));

                var value = MapToRange(noise, absVal, absValInv, min, max);

                if (isAlpha)
                {
                    value = MathUtils.Saturate(value);
                }

                outputs[i] = value;
            }
        }

        /// <summary>
        /// Maps value noise in -1 to 1 onto [<paramref name="min"/>, <paramref name="max"/>]. Absolute
        /// noise covers the whole range from 0, signed noise is centred in it, and inverting flips either.
        /// </summary>
        internal static float MapToRange(float noise, bool absolute, bool inverted, float min, float max)
        {
            var absScale = absolute ? 1f : 0.5f;
            var normalized = absolute ? MathF.Abs(noise) : noise;

            if (inverted)
            {
                normalized = 1f - normalized;
            }

            var span = max - min;

            return min + ((1f - absScale) * span) + (absScale * span * normalized);
        }
    }
}
