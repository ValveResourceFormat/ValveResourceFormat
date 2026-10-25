using System.Numerics.Tensors;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Blends a rotation field toward the yaw of a transform input's forward axis: atan2 of the
    /// rotated forward's Y over X, shifted by pi so the range before the offset is [0, 2pi], plus
    /// <c>m_flRotOffset</c> degrees. The target angle is the same for every particle each frame;
    /// only the blend against each particle's current value differs.
    /// </summary>
    /// <remarks>
    /// <c>m_flSpinStrength</c> is not a spin rate: it multiplies the operator strength to form the
    /// lerp weight, unclamped, so values above 1 overshoot.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapTransformOrientationToYaw">C_OP_RemapTransformOrientationToYaw</seealso>
    class RemapTransformOrientationToYaw : ParticleFunctionOperator
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly ParticleField outputField = ParticleField.Yaw;
        private readonly float rotOffset;
        private readonly float spinStrength = 1f;

        public RemapTransformOrientationToYaw(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            rotOffset = parse.Float("m_flRotOffset", rotOffset);
            spinStrength = parse.Float("m_flSpinStrength", spinStrength);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var rotation = Quaternion.CreateFromRotationMatrix(transformInput.NextTransform(particleSystemState));
            var forward = Vector3.Transform(Vector3.UnitX, rotation);
            var yaw = ParticleMath.Heading(forward) + float.DegreesToRadians(rotOffset);
            var weight = strength * spinStrength;

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var output = particles.Current.Floats(outputField);

            TensorPrimitives.Multiply(output, 1f - weight, output);
            TensorPrimitives.Add(output, yaw * weight, output);
        }
    }
}
