using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Turns each particle's input vector into its pitch, yaw and roll, as the frame that points along
    /// the vector with world up keeping it level, then turned by a further rotation input in that frame.
    /// The rotation input's components are roll, pitch and yaw in degrees.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapVectorToRotations">C_OP_RemapVectorToRotations</seealso>
    class RemapVectorToRotations : ParticleFunctionOperator
    {
        private readonly IVectorProvider input = new LiteralVectorProvider(Vector3.UnitX);
        private readonly IVectorProvider rotation = new LiteralVectorProvider(Vector3.Zero);

        public RemapVectorToRotations(ParticleDefinitionParser parse) : base(parse)
        {
            input = parse.VectorProvider("m_vecInput", input);
            rotation = parse.VectorProvider("m_vecRotation", rotation);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Pitch.Mask() | ParticleField.Yaw.Mask() | ParticleField.Roll.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            current.FillVectorInputs(input, rotation, particleSystemState, out var inputs, out var rotations);

            var pitches = current.Floats(ParticleField.Pitch);
            var yaws = current.Floats(ParticleField.Yaw);
            var rolls = current.Floats(ParticleField.Roll);

            for (var i = 0; i < inputs.Length; i++)
            {
                var vector = inputs[i];
                var direction = vector.Length() > 1e-5f ? Vector3.Normalize(vector) : vector;
                var (left, up) = ParticleMath.VectorVectors(direction);

                // The first row is the input as given, so an input that is not unit length skews the rotation
                var frame = Quaternion.CreateFromRotationMatrix(MathUtils.CreateBasis(vector, left, up));
                var offset = EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(rotations[i].Y, rotations[i].Z, rotations[i].X));
                var angles = Vector3.DegreesToRadians(EntityTransformHelper.ToEulerAngles(frame * offset));

                pitches[i] = angles.X;
                yaws[i] = angles.Y;
                rolls[i] = angles.Z;
            }
        }
    }
}
