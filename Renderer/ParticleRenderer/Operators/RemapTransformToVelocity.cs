namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Replaces every particle's velocity with the transform input's position read directly as a
    /// velocity vector in units per second, encoded into the Verlet state through
    /// <see cref="Particle.PositionPrevious"/>. No position delta is tracked between frames: the
    /// transform is meant to be a control point used as a vector holder, not a moving point, and
    /// its orientation is unused.
    /// </summary>
    /// <remarks>
    /// The engine implementation never reads operator strength, so the strength passed to
    /// <see cref="Operate"/> is ignored and the prior velocity is fully discarded.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapTransformToVelocity">C_OP_RemapTransformToVelocity</seealso>
    class RemapTransformToVelocity : ParticleFunctionOperator
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();

        public RemapTransformToVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var step = transformInput.NextTransform(particleSystemState).Translation * frameTime;

            foreach (ref var particle in particles.Current)
            {
                particle.PositionPrevious = particle.Position - step;
            }
        }
    }
}
