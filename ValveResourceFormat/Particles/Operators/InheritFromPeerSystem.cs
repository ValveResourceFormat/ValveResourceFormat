namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Copies an attribute from the particles of a peer, a sibling system under the same parent
    /// tagged with <c>m_nGroupID</c>, into this system's particles, one peer particle per particle.
    /// The walk over the peer's particles carries on from step to step, and once a peer runs out
    /// the next matching sibling takes over, wrapping to the first. Integers are copied as is, color
    /// and alpha clamped to [0, 1]. <c>m_nIncrement</c> is not read: the walk always advances by one.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_InheritFromPeerSystem">C_OP_InheritFromPeerSystem</seealso>
    class InheritFromPeerSystem : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly ParticleField fieldInput = ParticleField.Radius;
        private readonly int groupId;

        private ParticleSystemState? peer;
        private int peerParticle;

        public InheritFromPeerSystem(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            groupId = parse.Int32("m_nGroupID", groupId);
        }

        public override void Reset()
        {
            peer = null;
            peerParticle = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var peerParticles = ParticlesOf(peer);

                if (peerParticle >= peerParticles.Length)
                {
                    peer = FindNextPeer(particleSystemState);
                    peerParticle = 0;
                    peerParticles = ParticlesOf(peer);

                    if (peerParticles.IsEmpty)
                    {
                        return;
                    }
                }

                InheritFromParentParticles.CopyAttribute(ref particle, ref peerParticles[peerParticle], fieldInput, fieldOutput, 1f);
                peerParticle++;
            }
        }

        private static Span<Particle> ParticlesOf(ParticleSystemState? state)
            => state?.Data is { } simulation ? simulation.CurrentParticles : [];

        /// <summary>
        /// The first sibling tagged with the group after the current peer, wrapping round to the
        /// first sibling once, or null when there is none.
        /// </summary>
        private ParticleSystemState? FindNextPeer(ParticleSystemState particleSystemState)
        {
            var siblings = particleSystemState.ParentSystem?.Children;

            if (siblings == null)
            {
                return null;
            }

            var start = peer == null ? 0 : siblings.IndexOf(peer) + 1;

            for (var i = 0; i < siblings.Count; i++)
            {
                var candidate = siblings[(start + i) % siblings.Count];

                if (candidate != particleSystemState && ParticleSystemSimulation.IsInGroup(candidate, groupId))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
