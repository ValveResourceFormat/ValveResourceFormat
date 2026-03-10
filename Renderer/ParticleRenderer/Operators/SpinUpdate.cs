namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Integrates each particle's rotation speed into its rotation each frame. This is the only
    /// operator that advances rotation based on the rotation speed attribute.
    /// </summary>
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
                var rotationRadians = Vector3.DegreesToRadians(particle.RotationSpeed);
                particle.Rotation += rotationRadians * frameTime;
            }
        }
    }
}
