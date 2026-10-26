using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles at random positions within a box defined by a minimum and maximum corner
    /// vector, around a control point where it was at each particle's creation time, optionally in the
    /// control point's own axes.
    /// </summary>
    /// <remarks>
    /// <c>m_bUseNewCode</c> is not read. The game evaluates <c>m_vecMin</c> three more times with it set,
    /// which takes extra draws when that input is random.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinBox">C_INIT_CreateWithinBox</seealso>
    class CreateWithinBox : ParticleFunctionInitializer
    {
        private readonly IVectorProvider min = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider max = new LiteralVectorProvider(Vector3.Zero);

        private readonly int controlPointNumber;
        private readonly bool localSpace;
        private readonly RangeSampler rangeSampler;

        public CreateWithinBox(ParticleDefinitionParser parse) : base(parse)
        {
            min = parse.VectorProvider("m_vecMin", min);
            max = parse.VectorProvider("m_vecMax", max);
            controlPointNumber = parse.ControlPoint("m_nControlPointNumber", controlPointNumber);
            localSpace = parse.Boolean("m_bLocalSpace", localSpace);
            rangeSampler = RangeSampler.Parse(parse);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var positions = run.Vectors(ParticleField.Position);
            var previous = SpawnPreviousPositions(run, particleSystemState);
            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);

            run.FillVectorInputs(min, max, particleSystemState, out var mins, out var maxs);

            for (var i = 0; i < run.Length; i++)
            {
                var position = rangeSampler.NextVectorBetween(run.Start + i, particleSystemState, mins[i], maxs[i]);

                position = localSpace
                    ? Vector3.Transform(position, controlPoint.GetBasisAtTime(creationTimes[i]))
                    : position + controlPoint.GetPositionAtTime(creationTimes[i]);

                positions[i] = position;

                if (!previous.IsEmpty)
                {
                    previous[i] = position;
                }
            }
        }
    }
}
