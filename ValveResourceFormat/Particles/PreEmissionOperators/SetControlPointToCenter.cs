namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Moves a control point to the middle of whatever the system currently occupies, so operators
    /// reading that control point pull toward the effect itself rather than toward a fixed point.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointToCenter">C_OP_SetControlPointToCenter</seealso>
    class SetControlPointToCenter : ParticleFunctionPreEmissionOperator
    {
        private readonly int cp1 = 1;
        private readonly Vector3 cp1Pos = Vector3.Zero;

        /// <summary>
        /// Averages the live particle positions instead of taking the middle of the bounds. The
        /// bounds still stand in whenever no particle is alive to average.
        /// </summary>
        private readonly bool useAvgParticlePos;

        public SetControlPointToCenter(ParticleDefinitionParser parse) : base(parse)
        {
            cp1 = parse.Int32("m_nCP1", cp1);
            cp1Pos = parse.Vector3("m_vecCP1Pos", cp1Pos);
            useAvgParticlePos = parse.Boolean("m_bUseAvgParticlePos", useAvgParticlePos);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime)
        {
            var simulation = particleSystemState.Data;

            if (simulation == null)
            {
                return;
            }

            var particles = simulation.CurrentParticles;
            Vector3 center;

            if (useAvgParticlePos && particles.Length > 0)
            {
                var sum = Vector3.Zero;

                foreach (ref var particle in particles)
                {
                    sum += particle.Position;
                }

                center = sum / particles.Length;
            }
            else
            {
                // The bounds are kept relative to the system's own control point, and are the value
                // last calculated rather than one gathered here.
                center = simulation.LocalBoundingBox.Center + simulation.MainControlPoint.Position;
            }

            particleSystemState.SetControlPointValue(cp1, center + cp1Pos);
        }
    }
}
