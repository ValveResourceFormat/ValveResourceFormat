namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Picks a random subset of the child systems tagged with a group id and suppresses the rest.
    /// Used to vary an effect between plays, for example choosing one muzzle flash variant per shot.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ChooseRandomChildrenInGroup">C_OP_ChooseRandomChildrenInGroup</seealso>
    class ChooseRandomChildrenInGroup : ParticleFunctionPreEmissionOperator
    {
        private readonly int childGroupId = 1;
        private readonly INumberProvider numberOfChildren = new LiteralNumberProvider(1f);

        public ChooseRandomChildrenInGroup(ParticleDefinitionParser parse) : base(parse)
        {
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
            numberOfChildren = parse.NumberProvider("m_flNumberOfChildren", numberOfChildren);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime)
        {
            var count = (int)numberOfChildren.NextNumber(particleSystemState);

            particleSystemState.Data?.ChooseRandomChildrenInGroup(childGroupId, count);
        }
    }
}
