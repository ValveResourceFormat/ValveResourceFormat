namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Raises an event for each particle whose input value rises to the rising edge, and another once
    /// it falls back to the falling edge, for the children that emit from them. Each operator of this
    /// class keeps its own bit of the particle's user event states.
    /// </summary>
    /// <remarks>
    /// Only the first of these operators in a system tracks whether its particles are past the edge.
    /// Each later one raises its rising event on every step the input is at or above the edge, and
    /// never its falling event.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetUserEvent">C_OP_SetUserEvent</seealso>
    class SetUserEvent : ParticleFunctionOperator
    {
        private readonly INumberProvider input = new LiteralNumberProvider(0f);
        private readonly INumberProvider risingEdge = new LiteralNumberProvider(0f);
        private readonly ParticleEventType risingEventType = ParticleEventType.PARTICLE_EVENT_TYPE_MASK_USER_1;
        private readonly INumberProvider fallingEdge = new LiteralNumberProvider(0f);
        private readonly ParticleEventType fallingEventType = ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;

        private int userEventBit = 1;
        private readonly List<int> risen = [];

        public SetUserEvent(ParticleDefinitionParser parse) : base(parse)
        {
            input = parse.NumberProvider("m_flInput", input);
            risingEdge = parse.NumberProvider("m_flRisingEdge", risingEdge);
            risingEventType = parse.Enum("m_nRisingEventType", risingEventType);
            fallingEdge = parse.NumberProvider("m_flFallingEdge", fallingEdge);
            fallingEventType = parse.Enum("m_nFallingEventType", fallingEventType);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            var earlier = 0;

            foreach (var particleOperator in particleSystemState.Data?.Operators ?? [])
            {
                if (particleOperator == this)
                {
                    break;
                }

                if (particleOperator is SetUserEvent)
                {
                    earlier++;
                }
            }

            userEventBit = 1 << earlier;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.UserEventStates.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var risingListened = risingEventType != ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE && particleSystemState.ListensFor(risingEventType);
            var fallingListened = fallingEventType != ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE && particleSystemState.ListensFor(fallingEventType);

            if (!risingListened && !fallingListened)
            {
                return;
            }

            var tracksState = userEventBit == 1;
            risen.Clear();

            var current = particles.Current;
            var values = input.Fill(current, particleSystemState);
            var risingEdges = risingEdge.Fill(current, particleSystemState);
            var fallingEdges = fallingEdge.Fill(current, particleSystemState);

            var states = current.Integers(ParticleField.UserEventStates);

            for (var i = 0; i < states.Length; i++)
            {
                var value = values[i];
                var isSet = tracksState && (states[i] & userEventBit) != 0;

                // The first operator also re-raises the rising event for risen particles in the same group of four as a
                // newly rising one; each particle is decided on its own here
                if (risingEdges[i] <= value && !isSet)
                {
                    risen.Add(i);
                    states[i] |= userEventBit;
                }
                else if (value <= fallingEdges[i] && isSet)
                {
                    if (fallingListened)
                    {
                        particles.RaiseEvent(i, fallingEventType);
                    }

                    states[i] &= ~userEventBit;
                }
            }

            if (!risingListened)
            {
                return;
            }

            foreach (var index in risen)
            {
                particles.RaiseEvent(index, risingEventType);
            }
        }
    }
}
