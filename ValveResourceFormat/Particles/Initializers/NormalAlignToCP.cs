using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the particle normal to one axis of a transform as it stood at the particle's creation time,
    /// so ALIGN_TO_PARTICLE_NORMAL sprites face the way that axis points.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_NormalAlignToCP">C_INIT_NormalAlignToCP</seealso>
    class NormalAlignToCP : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly ParticleControlPointAxis controlPointAxis = ParticleControlPointAxis.PARTICLE_CP_AXIS_X;

        public NormalAlignToCP(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_transformInput", transformInput);
            controlPointAxis = parse.Enum("m_nControlPointAxis", controlPointAxis);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Normal);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var axis = controlPointAxis switch
            {
                ParticleControlPointAxis.PARTICLE_CP_AXIS_Y => Vector3.UnitY,
                ParticleControlPointAxis.PARTICLE_CP_AXIS_Z => Vector3.UnitZ,
                ParticleControlPointAxis.PARTICLE_CP_AXIS_NEGATIVE_X => -Vector3.UnitX,
                ParticleControlPointAxis.PARTICLE_CP_AXIS_NEGATIVE_Y => -Vector3.UnitY,
                ParticleControlPointAxis.PARTICLE_CP_AXIS_NEGATIVE_Z => -Vector3.UnitZ,
                _ => Vector3.UnitX,
            };

            var transform = transformInput.NextTransformAtTime(ref particle, particleSystemState, particle.CreationTime);
            particle.Normal = MathUtils.SafeNormalize(Vector3.TransformNormal(axis, transform));

            return particle;
        }
    }
}
