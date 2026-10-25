using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Writes a transform input's orientation into a particle's pitch/yaw/roll rotations (or its
    /// forward direction into the normal) at spawn, plus a fixed rotation offset. Shares its math
    /// with the operator variant.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapTransformOrientationToRotations">C_INIT_RemapTransformOrientationToRotations</seealso>
    class RemapTransformOrientationToRotations : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly Vector3 rotationOffset = Vector3.Zero;
        private readonly bool useQuat;
        private readonly bool writeNormal;

        public RemapTransformOrientationToRotations(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            rotationOffset = parse.Vector3("m_vecRotation", rotationOffset);
            useQuat = parse.Boolean("m_bUseQuat", useQuat);
            writeNormal = parse.Boolean("m_bWriteNormal", writeNormal);
        }

        public override ulong WrittenFields => writeNormal
            ? ParticleField.Normal.Mask()
            : ParticleField.Roll.Mask() | ParticleField.Yaw.Mask() | ParticleField.Pitch.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            // The transform is evaluated once, so every particle of the call gets the same orientation
            var (anglesRadians, forward) = TransformOrientationMath.Compute(transformInput, particleSystemState, rotationOffset, useQuat);

            if (writeNormal)
            {
                run.Vectors(ParticleField.Normal).Fill(forward);
                return;
            }

            run.Floats(ParticleField.Pitch).Fill(anglesRadians.X);
            run.Floats(ParticleField.Yaw).Fill(anglesRadians.Y);
            run.Floats(ParticleField.Roll).Fill(anglesRadians.Z);
        }
    }
}
