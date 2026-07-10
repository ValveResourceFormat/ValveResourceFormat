namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Integrates each particle's rotation speed into its rotation each frame. This is the only
    /// operator that advances rotation based on the rotation speed attribute.
    /// </summary>
    /// <remarks>
    /// "Rotation Basic" in the particle editor: it enables rotation authored through the effect's
    /// base <c>rotation_speed</c> property or the "Rotation Speed Random" initializer. The
    /// "Rotation Spin Roll"/"Rotation Spin Yaw" operators rotate on their own and do not require it.
    /// </remarks>
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
