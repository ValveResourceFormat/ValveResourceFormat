using ValveResourceFormat.Renderer.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Remaps a transform input's position, component by component, from an input range to an
    /// output range at spawn, gated by an optional creation time window. An optional local-space
    /// transform rotates the output range by its orientation (its position is unused), and a
    /// non-0.5 bias reshapes the remapped output while clamping it into [0, 1].
    /// </summary>
    /// <remarks>
    /// Color-type outputs (color, alpha, alternate alpha, glow color, glow alpha) are clamped to
    /// [0, 1] and skip the offset and accelerate paths entirely. The offset paths also add the
    /// result to <see cref="Particle.PositionPrevious"/> whatever the output field, and combining
    /// accelerate with offset multiplies the written value by one plus the frame time.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapTransformToVector">C_INIT_RemapTransformToVector</seealso>
    class RemapTransformToVector : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly Vector3 inputMin = Vector3.Zero;
        private readonly Vector3 inputMax = Vector3.Zero;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.Zero;
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly ITransformProvider? localSpaceTransform;
        private readonly float startTime = -1f;
        private readonly float endTime = -1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool offset;
        private readonly bool accelerate;
        private readonly float remapBias = 0.5f;

        public RemapTransformToVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Vector3("m_vInputMin", inputMin);
            inputMax = parse.Vector3("m_vInputMax", inputMax);
            outputMin = parse.Vector3("m_vOutputMin", outputMin);
            outputMax = parse.Vector3("m_vOutputMax", outputMax);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);

            if (parse.Data.ContainsKey("m_LocalSpaceTransform")
                && parse.Data.GetSubCollection("m_LocalSpaceTransform").GetStringProperty("m_nType", "PT_TYPE_INVALID") != "PT_TYPE_INVALID")
            {
                localSpaceTransform = parse.TransformInput("m_LocalSpaceTransform", new ControlPointTransformProvider());
            }

            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            offset = parse.Boolean("m_bOffset", offset);
            accelerate = parse.Boolean("m_bAccelerate", accelerate);
            remapBias = parse.Float("m_flRemapBias", remapBias);
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        /// <summary>
        /// The time window applies only when both bounds are set: a -1 sentinel on either bound
        /// disables the gate entirely.
        /// </summary>
        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            if (startTime != -1f && endTime != -1f
                && (particle.CreationTime < startTime || particle.CreationTime >= endTime))
            {
                return particle;
            }

            var position = transformInput.NextTransform(particleSystemState).Translation;
            var outMin = outputMin;
            var outMax = outputMax;

            if (localSpaceTransform != null)
            {
                var rotation = Quaternion.CreateFromRotationMatrix(localSpaceTransform.NextTransform(particleSystemState));
                outMin = Vector3.Transform(outMin, rotation);
                outMax = Vector3.Transform(outMax, rotation);
            }

            var value = new Vector3(
                RemapComponent(position.X, inputMin.X, inputMax.X, outMin.X, outMax.X),
                RemapComponent(position.Y, inputMin.Y, inputMax.Y, outMin.Y, outMax.Y),
                RemapComponent(position.Z, inputMin.Z, inputMax.Z, outMin.Z, outMax.Z));

            if (remapBias != 0.5f)
            {
                value = new Vector3(
                    NumericBias.Standard(MathUtils.Saturate(value.X), remapBias),
                    NumericBias.Standard(MathUtils.Saturate(value.Y), remapBias),
                    NumericBias.Standard(MathUtils.Saturate(value.Z), remapBias));
            }

            value = particle.ModifyVectorBySetMethodAtSpawn(particles, fieldOutput, value, setMethod);

            if (fieldOutput is ParticleField.Color or ParticleField.Alpha or ParticleField.AlphaAlternate
                or ParticleField.GlowRgb or ParticleField.GlowAlpha)
            {
                particle.SetVector(fieldOutput, Vector3.Clamp(value, Vector3.Zero, Vector3.One));
            }
            else if (accelerate && !offset)
            {
                particle.SetVector(fieldOutput, value * particles.CurrentFrameTime);
            }
            else if (offset)
            {
                value += particle.GetVector(fieldOutput);
                particle.PositionPrevious += value;
                particle.SetVector(fieldOutput, accelerate ? value * (1f + particles.CurrentFrameTime) : value);
            }
            else
            {
                particle.SetVector(fieldOutput, value);
            }

            return particle;
        }

        private static float RemapComponent(float input, float inputMin, float inputMax, float outputMin, float outputMax)
            => inputMin == inputMax
                ? (input - inputMax < 0f ? outputMin : outputMax)
                : outputMin + (MathUtils.Saturate((input - inputMin) / (inputMax - inputMin)) * (outputMax - outputMin));
    }
}
