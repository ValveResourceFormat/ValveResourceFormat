using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Writes each particle's spawn direction from the transform input's position into a vector
    /// field, rotated about <c>m_vecOffsetAxis</c> by <c>m_flOffsetRot</c> degrees, optionally
    /// normalized, and scaled by <c>m_flScale</c>. Only the transform's position is used; its
    /// orientation is ignored.
    /// </summary>
    /// <remarks>
    /// The axis is not normalized, so the default zero axis with a non-zero angle scales uniformly
    /// by the angle's cosine instead.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapInitialDirectionToTransformToVector">C_INIT_RemapInitialDirectionToTransformToVector</seealso>
    class RemapInitialDirectionToTransformToVector : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly float scale = 1f;
        private readonly bool normalize;
        private readonly Vector3 rotationRowX;
        private readonly Vector3 rotationRowY;
        private readonly Vector3 rotationRowZ;

        public RemapInitialDirectionToTransformToVector(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            scale = parse.Float("m_flScale", scale);
            var rotationDegrees = parse.Float("m_flOffsetRot", 0f);
            var axis = parse.Vector3("m_vecOffsetAxis", Vector3.Zero);
            normalize = parse.Boolean("m_bNormalize", normalize);

            var (sin, cos) = MathF.SinCos(float.DegreesToRadians(rotationDegrees));
            var reverse = 1f - cos;
            rotationRowX = new Vector3(
                ((1f - (axis.X * axis.X)) * cos) + (axis.X * axis.X),
                (axis.X * axis.Y * reverse) - (axis.Z * sin),
                (axis.X * axis.Z * reverse) + (axis.Y * sin));
            rotationRowY = new Vector3(
                (axis.Y * axis.X * reverse) + (axis.Z * sin),
                ((1f - (axis.Y * axis.Y)) * cos) + (axis.Y * axis.Y),
                (axis.Y * axis.Z * reverse) - (axis.X * sin));
            rotationRowZ = new Vector3(
                (axis.X * axis.Z * reverse) - (axis.Y * sin),
                (axis.Y * axis.Z * reverse) + (axis.X * sin),
                ((1f - (axis.Z * axis.Z)) * cos) + (axis.Z * axis.Z));
        }

        public override ulong WrittenFields => fieldOutput.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var origin = transformInput.NextTransform(particleSystemState).Translation;

            if (!ParticleBuffer.IsVector(fieldOutput))
            {
                return;
            }

            var positions = run.ReadVectors(ParticleField.Position);
            var outputs = run.Vectors(fieldOutput);

            // Only the new particles are written, on purpose: a batch of 16 or more does not rewrite the
            // field of every live particle, which without m_bNormalize would also divide by the previous step
            for (var i = 0; i < outputs.Length; i++)
            {
                var delta = positions[i] - origin;
                var direction = new Vector3(
                    Vector3.Dot(delta, rotationRowX),
                    Vector3.Dot(delta, rotationRowY),
                    Vector3.Dot(delta, rotationRowZ));

                if (normalize)
                {
                    direction = MathUtils.SafeNormalize(direction);
                }

                outputs[i] = direction * scale;
            }
        }
    }
}
