namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Moves a control point to the middle of whatever the system currently occupies, so operators
    /// reading that control point pull toward the effect itself rather than toward a fixed point.
    /// <c>m_nSetParent</c> writes it on the parent or root system instead, and through it on every
    /// system below that one.
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

        private readonly ParticleParentSetMode setParent = ParticleParentSetMode.PARTICLE_SET_PARENT_NO;

        public SetControlPointToCenter(ParticleDefinitionParser parse) : base(parse)
        {
            cp1 = parse.ControlPoint("m_nCP1", cp1);
            cp1Pos = parse.Vector3("m_vecCP1Pos", cp1Pos);
            useAvgParticlePos = parse.Boolean("m_bUseAvgParticlePos", useAvgParticlePos);
            setParent = parse.Enum("m_nSetParent", setParent);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
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
                // The bounds last calculated, kept when there was nothing to bound
                var bounds = simulation.WorldBounds;
                center = (bounds.Min + bounds.Max) * 0.5f;
            }

            particleSystemState.GetSetParentTarget(setParent).SetControlPointValue(cp1, center + cp1Pos);
        }
    }
}
