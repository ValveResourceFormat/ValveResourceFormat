namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Picks a random subset of the child systems tagged with a group id and restarts them, and stops
    /// the rest of the group. Used to vary an effect between plays, for example choosing one muzzle
    /// flash variant per shot.
    /// </summary>
    /// <remarks>
    /// The group is held back from the start, and the choice is made once per restart of the system,
    /// and not while it is stopped. A fractional count rounds up.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ChooseRandomChildrenInGroup">C_OP_ChooseRandomChildrenInGroup</seealso>
    class ChooseRandomChildrenInGroup : ParticleFunctionPreEmissionOperator
    {
        private readonly int childGroupId = 1;
        private readonly INumberProvider numberOfChildren = new LiteralNumberProvider(1f);

        private bool done;

        public ChooseRandomChildrenInGroup(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            numberOfChildren = parse.NumberProvider("m_flNumberOfChildren", numberOfChildren);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            done = false;
            particleSystemState.Data?.HoldChildrenInGroup(childGroupId);
        }

        public override void Restart(ParticleSystemState particleSystemState) => Initialize(particleSystemState);

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (particleSystemState.EmissionStopped || done || particleSystemState.Data == null)
            {
                return;
            }

            particleSystemState.Data.ChooseRandomChildrenInGroup(childGroupId, numberOfChildren.NextNumber(particleSystemState));
            done = true;
        }
    }
}
