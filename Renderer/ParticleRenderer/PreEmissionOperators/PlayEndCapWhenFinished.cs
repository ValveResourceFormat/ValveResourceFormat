namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Starts the endcap once the system has run out. Without <c>m_bFireOnEmissionEnd</c> it waits for
    /// the last particle to be one step from death; with it, the end of emission is enough.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PlayEndCapWhenFinished">C_OP_PlayEndCapWhenFinished</seealso>
    class PlayEndCapWhenFinished : ParticleFunctionPreEmissionOperator
    {
        private readonly bool fireOnEmissionEnd;

        /// <summary>Defaults on; content only ever authors this key to turn it off.</summary>
        private readonly bool includeChildren = true;

        private bool fired;

        public PlayEndCapWhenFinished(ParticleDefinitionParser parse) : base(parse)
        {
            fireOnEmissionEnd = parse.Boolean("m_bFireOnEmissionEnd", fireOnEmissionEnd);
            includeChildren = parse.Boolean("m_bIncludeChildren", includeChildren);
        }

        public override void Reset()
        {
            fired = false;
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (fired || particleSystemState.Data == null)
            {
                return;
            }

            if (!particleSystemState.Data.HasFinishedEmitting(fireOnEmissionEnd, includeChildren))
            {
                return;
            }

            fired = true;
            particleSystemState.Data.PlayEndCap();
        }
    }
}
