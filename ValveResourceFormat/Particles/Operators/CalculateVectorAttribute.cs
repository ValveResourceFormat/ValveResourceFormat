using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Writes a vector attribute built from a start value plus scaled control point positions and
    /// scaled input attributes, multiplied per component by a final scale. A normal output is
    /// normalized.
    /// </summary>
    /// <remarks>
    /// The second control point term is skipped when the first control point, not the second, is -1.
    /// Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CalculateVectorAttribute">C_OP_CalculateVectorAttribute</seealso>
    class CalculateVectorAttribute : ParticleFunctionOperator
    {
        private readonly Vector3 startValue;
        private readonly ParticleField fieldInput1 = ParticleField.Position;
        private readonly float inputScale1 = 1f;
        private readonly ParticleField fieldInput2 = ParticleField.Position;
        private readonly float inputScale2;
        private readonly int controlPointInput1;
        private readonly float controlPointScale1 = -1f;
        private readonly int controlPointInput2;
        private readonly float controlPointScale2;
        private readonly ParticleField fieldOutput = ParticleField.Normal;
        private readonly Vector3 finalOutputScale = Vector3.One;

        public CalculateVectorAttribute(ParticleDefinitionParser parse) : base(parse)
        {
            startValue = parse.Vector3("m_vStartValue", startValue);
            fieldInput1 = parse.ParticleField("m_nFieldInput1", fieldInput1);
            inputScale1 = parse.Float("m_flInputScale1", inputScale1);
            fieldInput2 = parse.ParticleField("m_nFieldInput2", fieldInput2);
            inputScale2 = parse.Float("m_flInputScale2", inputScale2);
            controlPointInput1 = ReadControlPointReference(parse, "m_nControlPointInput1");
            controlPointScale1 = parse.Float("m_flControlPointScale1", controlPointScale1);
            controlPointInput2 = ReadControlPointReference(parse, "m_nControlPointInput2");
            controlPointScale2 = parse.Float("m_flControlPointScale2", controlPointScale2);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            finalOutputScale = parse.Vector3("m_vFinalOutputScale", finalOutputScale);
        }

        /// <summary>
        /// Reads the control point index of a <c>ControlPointReference_t</c>. Its offset and local space
        /// members are not used.
        /// </summary>
        private static int ReadControlPointReference(ParticleDefinitionParser parse, string key)
        {
            if (!parse.Data.ContainsKey(key))
            {
                return 0;
            }

            return parse.Data.GetSubCollection(key) is { IsCollection: true } reference
                ? parse.Nested(reference).Int32("m_controlPointNameString", 0)
                : parse.Int32(key, 0);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var baseValue = startValue;

            if (controlPointScale1 != 0f && controlPointInput1 != -1)
            {
                baseValue += controlPointScale1 * particleSystemState.GetControlPoint(controlPointInput1).Position;
            }

            if (controlPointScale2 != 0f && controlPointInput1 != -1)
            {
                baseValue += controlPointScale2 * particleSystemState.GetControlPoint(controlPointInput2).Position;
            }

            var normalize = fieldOutput == ParticleField.Normal;

            if (!ParticleBuffer.IsVector(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var outputs = current.Vectors(fieldOutput);
            var inputs1 = inputScale1 != 0f ? current.ReadVectors(fieldInput1) : default;
            var inputs2 = inputScale2 != 0f ? current.ReadVectors(fieldInput2) : default;

            for (var i = 0; i < outputs.Length; i++)
            {
                var value = baseValue;

                if (inputScale1 != 0f)
                {
                    value += inputScale1 * inputs1[i];
                }

                if (inputScale2 != 0f)
                {
                    value += inputScale2 * inputs2[i];
                }

                value *= finalOutputScale;

                if (normalize)
                {
                    var lengthSquared = value.LengthSquared();
                    value /= MathF.Sqrt(lengthSquared < ParticleMath.FloatMin ? ParticleMath.FloatEpsilon : lengthSquared);
                }

                outputs[i] = value;
            }
        }
    }
}
