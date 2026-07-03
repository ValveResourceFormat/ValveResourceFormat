namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Integrates each particle's rotation speed into its rotation each frame. This is the only
    /// operator that advances rotation based on the rotation speed attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SpinUpdate">C_OP_SpinUpdate</seealso>
    class SpinUpdate : ParticleFunctionOperator
    {
        public SpinUpdate(ParticleDefinitionParser parse) : base(parse)
        {
        }

        // This is the only place that will update Rotation based on RotationSpeed
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                // RotationSpeed is stored in radians per second by everything that writes it
                particle.Rotation += particle.RotationSpeed * frameTime;
            }
        }
    }
}
