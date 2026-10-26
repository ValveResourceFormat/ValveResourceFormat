namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Sets how fast the system runs, as <see cref="ParticleSystemState.TimeScale"/>. The new rate
    /// applies from the next update, and only the root system's rate scales the frame time.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetSimulationRate">C_OP_SetSimulationRate</seealso>
    class SetSimulationRate : ParticleFunctionPreEmissionOperator
    {
        private readonly INumberProvider simulationScale = new LiteralNumberProvider(1f);

        public SetSimulationRate(ParticleDefinitionParser parse) : base(parse)
        {
            simulationScale = parse.NumberProvider("m_flSimulationScale", simulationScale);
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            particleSystemState.TimeScale = simulationScale.NextNumber(particleSystemState);
        }
    }
}
