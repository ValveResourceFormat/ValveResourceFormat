namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Writes the gravity the game reports at a control point into the position and orientation of
    /// another one. Without a game there is no gravity to report, and the control point is left as it
    /// is.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetGravityToCP">C_OP_SetGravityToCP</seealso>
    class SetGravityToCP : ParticleFunctionPreEmissionOperator
    {
        public SetGravityToCP(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
        }
    }
}
