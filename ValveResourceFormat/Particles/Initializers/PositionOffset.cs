using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Adds a per-component random offset to the particle position. With local coordinates the
    /// offset is authored in a transform input's frame and rotates with it; when the proportional
    /// flag is set, the offset is scaled by the particle's radius.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionOffset">C_INIT_PositionOffset</seealso>
    class PositionOffset : ParticleFunctionInitializer
    {
        private readonly IVectorProvider offsetMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider offsetMax = new LiteralVectorProvider(Vector3.Zero);

        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly bool localCoords; // offset in local space 0/1
        private readonly bool proportional; // offset proportional to radius 0/1
        private readonly RangeSampler rangeSampler;

        public PositionOffset(ParticleDefinitionParser parse) : base(parse)
        {
            offsetMin = parse.VectorProvider("m_OffsetMin", offsetMin);
            offsetMax = parse.VectorProvider("m_OffsetMax", offsetMax);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            proportional = parse.Boolean("m_bProportional", proportional);
            rangeSampler = RangeSampler.Parse(parse);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particles, particleSystemState, blockRun: false);
        }

        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particles, particleSystemState, blockRun: true);
        }

        private void InitializeRun(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState, bool blockRun)
        {
            var rotation = localCoords ? transformInput.NextTransform(particleSystemState) : Matrix4x4.Identity;

            // The offsets are evaluated for every live particle, and each particle of the run reads its own value
            var posMins = offsetMin.Prepare(run, particles.Current, particleSystemState);
            var posMaxs = offsetMax.Prepare(run, particles.Current, particleSystemState);

            run.FillPerBlockInputs(particleSystemState, offsetMin, posMins, offsetMax, posMaxs, blocksFromStart: true);

            var positions = run.Vectors(ParticleField.Position);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);
            var radii = proportional ? run.ReadFloats(ParticleField.Radius) : default;

            for (var i = 0; i < positions.Length; i++)
            {
                // The block run takes the evenly distributed samples of each block of four in reverse
                var index = run.Start + i;
                var sampleIndex = blockRun ? index ^ 3 : index;

                var offset = rangeSampler.NextVectorBetween(sampleIndex, particleSystemState, posMins[i], posMaxs[i]);

                if (proportional)
                {
                    offset *= radii[i];
                }

                if (localCoords)
                {
                    offset = Vector3.TransformNormal(offset, rotation);
                }

                positions[i] += offset;
                previousPositions[i] += offset;
            }
        }
    }
}
