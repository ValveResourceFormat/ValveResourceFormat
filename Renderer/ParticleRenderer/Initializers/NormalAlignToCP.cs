namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets the particle normal to a control point's forward direction, so ALIGN_TO_PARTICLE_NORMAL sprites
    /// face the way the control point points instead of keeping their default up-facing normal.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_NormalAlignToCP">C_INIT_NormalAlignToCP</seealso>
    class NormalAlignToCP : ParticleFunctionInitializer
    {
        private readonly int ControlPointNumber;

        public NormalAlignToCP(ParticleDefinitionParser parse) : base(parse)
        {
            ControlPointNumber = parse.Int32("m_nControlPointNumber", 0);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var orientation = particleSystemState.GetControlPoint(ControlPointNumber).Orientation;

            // The control point orientation is a forward direction; zero means unset, so keep the default normal.
            if (orientation != Vector3.Zero)
            {
                particle.Normal = Vector3.Normalize(orientation);
            }

            return particle;
        }
    }
}
