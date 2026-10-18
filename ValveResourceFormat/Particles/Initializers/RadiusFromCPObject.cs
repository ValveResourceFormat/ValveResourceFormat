namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the radius of new particles to the scale of the object a control point is bound to, 1
    /// when it is bound to none, times the initializer's strength.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RadiusFromCPObject">C_INIT_RadiusFromCPObject</seealso>
    class RadiusFromCPObject : ParticleFunctionInitializer
    {
        private readonly int controlPoint;

        public RadiusFromCPObject(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPoint", controlPoint);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Radius);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var scale = particleSystemState.GetControlPoint(controlPoint).ResolveModelObject()?.Transform.MaxAxisScale() ?? 1f;
            particle.Radius = scale * GetOperatorRunStrength(particleSystemState);

            _ = particleSystemState.Random.Next();
            return particle;
        }
    }
}
