namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills the particles above a maximum count, lowest index first.
    /// </summary>
    /// <remarks>
    /// The count is truncated to a whole number. Unless the system keeps its particles in order, earlier
    /// kills move the newest particles into the low indices, so these are not always the oldest ones.
    /// Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DecayClampCount">C_OP_DecayClampCount</seealso>
    class DecayClampCount : ParticleFunctionOperator
    {
        private readonly INumberProvider count = new LiteralNumberProvider(100f);

        public DecayClampCount(ParticleDefinitionParser parse) : base(parse)
        {
            count = parse.CollectionNumberProvider("m_nCount", count);
        }

        public override bool IsOrderImportant => true;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var excess = Math.Clamp(current.Length - (int)count.NextNumber(particleSystemState), 0, current.Length);

            for (var i = 0; i < excess; i++)
            {
                particles.Kill(i);
            }
        }
    }
}
