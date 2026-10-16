using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Particle attribute remapping handler.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/CParticleFloatInput">CParticleFloatInput</seealso>
    class AttributeMapping
    {
        /// <summary>
        /// Input handling modes for attribute mapping operations.
        /// </summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleFloatInputMode_t">ParticleFloatInputMode_t</seealso>
        internal enum PfInputMode
        {
            /// <summary>Invalid input mode.</summary>
            Invalid = -1,
            /// <summary>Input values are clamped to the defined input range.</summary>
            Clamped,
            /// <summary>Input values wrap around within the defined input range.</summary>
            Looped,
        }

        /// <summary>
        /// Attribute mapping transformation types.
        /// </summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleFloatMapType_t">ParticleFloatMapType_t</seealso>
        internal enum PfMapType
        {
            /// <summary>Invalid mapping type.</summary>
            Invalid = -1,
            /// <summary>Passes the input value through unchanged.</summary>
            Direct,
            /// <summary>Multiplies the input value by a constant factor.</summary>
            Mult,
            /// <summary>Remaps the input from one range to another.</summary>
            Remap,
            /// <summary>Remaps the input with an additional bias curve applied.</summary>
            RemapBiased,
            /// <summary>Evaluates a piecewise curve at the input value.</summary>
            Curve,
            /// <summary>Returns one of two output values depending on whether the input is within a range.</summary>
            Notched,
            /// <summary>Rounds the input value using the configured rounding mode.</summary>
            Round,
            /// <summary>Takes the smaller of the input value and the compare value.</summary>
            Min,
            /// <summary>Takes the larger of the input value and the compare value.</summary>
            Max,
            /// <summary>Takes the remainder of the input value divided by the compare value, or the input itself when that is 0.</summary>
            Mod,
        };

        /// <summary>
        /// Rounding modes for <see cref="PfMapType.Round"/>.
        /// </summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleFloatRoundType_t">ParticleFloatRoundType_t</seealso>
        internal enum PfRoundType
        {
            /// <summary>Invalid rounding mode.</summary>
            Invalid = -1,
            /// <summary>Round to the nearest integer.</summary>
            Nearest,
            /// <summary>Round down.</summary>
            Floor,
            /// <summary>Round up.</summary>
            Ceil,
        }

        private readonly PfMapType mapType;
        private readonly PfInputMode inputMode = PfInputMode.Clamped;
        private readonly PfRoundType roundType = PfRoundType.Nearest;

        private readonly float multFactor = 1f;

        private readonly float input0;
        private readonly float input1 = 1f;
        private readonly float output0;
        private readonly float output1 = 1f;

        private readonly float notchedRangeMin;
        private readonly float notchedRangeMax = 1f;
        private readonly float notchedOutputOutside;
        private readonly float notchedOutputInside = 1f;

        private readonly ParticleFloatBiasType biasType;
        private readonly float biasParameter;

        private readonly PiecewiseCurve? curve;

        private readonly float compareValue;

        public AttributeMapping(ParticleDefinitionParser parse)
        {
            mapType = parse.EnumNormalized<PfMapType>("m_nMapType");
            inputMode = parse.EnumNormalized<PfInputMode>("m_nInputMode", inputMode);

            switch (mapType)
            {
                case PfMapType.Direct:
                    break;

                case PfMapType.Mult:
                    multFactor = parse.Float("m_flMultFactor", multFactor);
                    break;

                case PfMapType.Remap:
                case PfMapType.RemapBiased:
                    input0 = parse.Float("m_flInput0", input0);
                    input1 = parse.Float("m_flInput1", input1);
                    output0 = parse.Float("m_flOutput0", output0);
                    output1 = parse.Float("m_flOutput1", output1);
                    biasType = parse.Enum("m_nBiasType", biasType);
                    biasParameter = parse.Float("m_flBiasParameter", biasParameter);
                    break;

                case PfMapType.Curve:
                    var curveData = parse.Data.GetSubCollection("m_Curve");
                    curve = new PiecewiseCurve(curveData, inputMode == PfInputMode.Looped);
                    break;

                case PfMapType.Notched:
                    var rangeMin = parse.Float("m_flNotchedRangeMin", notchedRangeMin);
                    var rangeMax = parse.Float("m_flNotchedRangeMax", notchedRangeMax);
                    notchedRangeMin = MathF.Min(rangeMin, rangeMax);
                    notchedRangeMax = MathF.Max(rangeMin, rangeMax);
                    notchedOutputOutside = parse.Float("m_flNotchedOutputOutside", notchedOutputOutside);
                    notchedOutputInside = parse.Float("m_flNotchedOutputInside", notchedOutputInside);
                    break;

                case PfMapType.Round:
                    roundType = parse.Data.ContainsKey("m_nRoundType")
                        ? parse.EnumNormalized("m_nRoundType", PfRoundType.Invalid)
                        : roundType;
                    break;

                case PfMapType.Min:
                case PfMapType.Max:
                case PfMapType.Mod:
                    compareValue = parse.Float("m_flCompareValue");
                    break;

                default:
                    break;

            }
        }

        public float ApplyMapping(float value)
        {
            switch (mapType)
            {
                case PfMapType.Mult:
                    return value * multFactor;

                case PfMapType.Remap:
                    return ClampToOutputRange((RemapFraction(value) * (output1 - output0)) + output0);

                case PfMapType.RemapBiased:
                    var biased = ParticleMath.BiasFromParameter(MathF.Min(RemapFraction(value), 1f), biasParameter, biasType);
                    return ClampToOutputRange((biased * (output1 - output0)) + output0);

                case PfMapType.Curve:
                    return curve!.Evaluate(value);

                case PfMapType.Notched:
                    return value >= notchedRangeMin && value <= notchedRangeMax
                        ? notchedOutputInside
                        : notchedOutputOutside;

                case PfMapType.Round:
                    return roundType switch
                    {
                        PfRoundType.Nearest => MathF.Round(value),
                        PfRoundType.Floor => MathF.Floor(value),
                        PfRoundType.Ceil => MathF.Ceiling(value),
                        _ => value,
                    };

                case PfMapType.Min:
                    return MathF.Min(value, compareValue);

                case PfMapType.Max:
                    return MathF.Max(value, compareValue);

                case PfMapType.Mod:
                    return compareValue == 0f ? value : value - (MathF.Truncate(value / compareValue) * compareValue);

                default:
                    return value;
            }
        }

        /// <summary>
        /// Where the input falls in the input range, unclamped. An empty range is a step at its single
        /// value. A looped input is first folded into <c>[0, m_flInput1)</c>, so a range that does not
        /// start at zero still wraps at its authored upper end.
        /// </summary>
        private float RemapFraction(float value)
        {
            if (inputMode == PfInputMode.Looped && input1 != 0f)
            {
                value = input1 * MathUtils.Fract(value / input1);
            }

            var inputRange = input1 - input0;

            if (inputRange == 0f)
            {
                return input0 <= value ? 1f : 0f;
            }

            return (value - input0) / inputRange;
        }

        private float ClampToOutputRange(float value)
            => MathUtils.Clamp(value, output0, output1);
    }
}
