namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Launches a particle along its normal at a random speed.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_VelocityFromNormal">C_INIT_VelocityFromNormal</seealso>
    class VelocityFromNormal : ParticleFunctionInitializer
    {
        private readonly float speedMin;
        private readonly float speedMax;
        private readonly bool ignoreDt;

        public VelocityFromNormal(ParticleDefinitionParser parse) : base(parse)
        {
            speedMin = parse.Float("m_fSpeedMin", speedMin);
            speedMax = parse.Float("m_fSpeedMax", speedMax);
            ignoreDt = parse.Boolean("m_bIgnoreDt", ignoreDt);
        }

        public override ulong WrittenFields => ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var normals = run.ReadVectors(ParticleField.Normal);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);

            for (var i = 0; i < previousPositions.Length; i++)
            {
                var velocity = normals[i] * particleSystemState.Random.NextBetween(speedMin, speedMax);

                previousPositions[i] -= VelocityStep(particleSystemState, velocity, ignoreDt);
            }
        }
    }
}
