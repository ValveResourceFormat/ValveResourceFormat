using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
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

        public override ulong WrittenFields => ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, run, particleSystemState, blockRun: false);
        }

        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            // The inputs are evaluated for every live particle, and each particle of the run reads its own value
            // rather than the value of the particle as far into the collection as it is into the run
            InitializeRun(run, particles.Current, particleSystemState, blockRun: true);
        }

        /// <summary>Initializes <paramref name="run"/> with the inputs set up over <paramref name="evaluated"/>, which contains it.</summary>
        private void InitializeRun(ParticleSpan run, ParticleSpan evaluated, ParticleSystemState particleSystemState, bool blockRun)
        {
            var offsetLocs = offsetLoc.Prepare(run, evaluated, particleSystemState);
            var mins = outputMin.Prepare(run, evaluated, particleSystemState);
            var maxs = outputMax.Prepare(run, evaluated, particleSystemState);

            var offsets = offset.Fill(run, evaluated, particleSystemState);
            var noiseScales = noiseScale.Fill(run, evaluated, particleSystemState);
            var noiseScaleLocs = noiseScaleLoc.Fill(run, evaluated, particleSystemState);

            foreach (var (blockOffset, block, lane) in run.InputBlocks())
            {
                offsetLoc.FillPerBlock(block, lane, particleSystemState, offsetLocs[blockOffset..]);
                outputMin.FillPerBlock(block, lane, particleSystemState, mins[blockOffset..]);
                outputMax.FillPerBlock(block, lane, particleSystemState, maxs[blockOffset..]);
            }

            var positions = run.ReadVectors(ParticleField.Position);
            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);
            var anyInverted = absValInv != Vector3.Zero;

            for (var i = 0; i < previousPositions.Length; i++)
            {
                // The noise coordinate is spatial (particle creation position) plus a time term, so
                // particles emitted the same tick at different positions get decorrelated velocities.
                var samplePosition = ((positions[i] + offsetLocs[i]) * noiseScaleLocs[i])
                    + new Vector3((creationTimes[i] + offsets[i]) * noiseScales[i]);
                var noise = new Vector3(
                    Noise.Value3D(samplePosition),
                    Noise.Value3D(samplePosition + new Vector3(100000.5f, 300000.25f, 9000001f)),
                    Noise.Value3D(samplePosition + new Vector3(110000.25f, 310000.75f, 9100000f)));

                var min = mins[i];
                var max = maxs[i];
                var velocity = new Vector3(
                    MapComponent(noise.X, absVal.X != 0f, absValInv.X != 0f, anyInverted, blockRun, min.X, max.X),
                    MapComponent(noise.Y, absVal.Y != 0f, absValInv.Y != 0f, anyInverted, blockRun, min.Y, max.Y),
                    MapComponent(noise.Z, absVal.Z != 0f, absValInv.Z != 0f, anyInverted, blockRun, min.Z, max.Z));

                var transform = transformInput.NextTransformAtTime(particleSystemState, creationTimes[i]);
                var transformed = Vector3.TransformNormal(velocity, transform);

                previousPositions[i] -= VelocityStep(particleSystemState, transformed, ignoreDt);
            }
        }

        /// <summary>
        /// Maps one noise component into the output range. When any component is inverted, inverted
        /// components become 1 - noise in whole blocks of four, while the particles outside them take
        /// 1 + noise and have every other component sign-flipped, exceeding the usual output band.
        /// </summary>
        private static float MapComponent(float noise, bool absolute, bool inverted, bool anyInverted, bool blockRun, float min, float max)
        {
            var value = absolute ? MathF.Abs(noise) : noise;

            if (anyInverted)
            {
                if (blockRun)
                {
                    value = inverted ? 1f - value : value;
                }
                else
                {
                    // The per-particle path swaps the inversion's multiplier and offset, unlike the block path
                    value = inverted ? 1f + value : -value;
                }
            }

            var absScale = absolute ? 1f : 0.5f;
            var span = max - min;

            return (value * span * absScale) + min + (span * (1f - absScale));
        }
    }
}
