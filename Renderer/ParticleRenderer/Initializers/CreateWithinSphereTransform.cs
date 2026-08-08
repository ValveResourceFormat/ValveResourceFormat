namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// A <see cref="CreateWithinSphere"/> that centres and orients its sphere on a transform input
    /// instead of on control point 0.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinSphereTransform">C_INIT_CreateWithinSphereTransform</seealso>
    class CreateWithinSphereTransform : CreateWithinSphere
    {
        public CreateWithinSphereTransform(ParticleDefinitionParser parse)
            : base(parse, parse.TransformInput("m_TransformInput", new ControlPointTransformProvider()))
        {
        }
    }
}
