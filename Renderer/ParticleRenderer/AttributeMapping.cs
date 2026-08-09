using ValveResourceFormat.Renderer.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle attribute remapping handler.
    /// </summary>
    class AttributeMapping
    {
        /// <summary>
        /// Input handling modes for attribute mapping operations.
        /// </summary>
        public enum PfInputMode
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
        public enum PfMapType
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
        };

        /// <summary>
        /// Rounding modes for <see cref="PfMapType.Round"/>.
        /// </summary>
        public enum PfRoundType
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

        private readonly float multFactor;

        private readonly float input0;
        private readonly float input1;
        private readonly float output0;
        private readonly float output1;

        private readonly float notchedRangeMin;
        private readonly float notchedRangeMax;
        private readonly float notchedOutputOutside;
        private readonly float notchedOutputInside;

        private readonly ParticleFloatBiasType biasType;
        private readonly float biasParameter;

        private readonly PiecewiseCurve? curve;


        public AttributeMapping(ParticleDefinitionParser parse)
        {
            mapType = parse.EnumNormalized<PfMapType>("m_nMapType");
            inputMode = parse.EnumNormalized<PfInputMode>("m_nInputMode", inputMode);

            switch (mapType)
            {
                case PfMapType.Direct:
                    break;

                case PfMapType.Mult:
                    multFactor = parse.Float("m_flMultFactor");
                    break;

                case PfMapType.Remap:
                    input0 = parse.Float("m_flInput0");
                    input1 = parse.Float("m_flInput1");
                    output0 = parse.Float("m_flOutput0");
                    output1 = parse.Float("m_flOutput1");

                    // Sort the input range, swapping the outputs with it so the authored
                    // input->output pairing (which may deliberately descend) is preserved
                    if (input0 > input1)
                    {
                        (input0, input1) = (input1, input0);
                        (output0, output1) = (output1, output0);
                    }

                    break;

                case PfMapType.RemapBiased:
                    input0 = parse.Float("m_flInput0");
                    input1 = parse.Float("m_flInput1");
                    output0 = parse.Float("m_flOutput0");
                    output1 = parse.Float("m_flOutput1");
                    biasType = parse.Enum<ParticleFloatBiasType>("m_nBiasType");
                    biasParameter = parse.Float("m_flBiasParameter");
                    break;

                case PfMapType.Curve:
                    var curveData = parse.Data.GetSubCollection("m_Curve");
                    curve = new PiecewiseCurve(curveData, inputMode == PfInputMode.Looped);
                    break;

                case PfMapType.Notched:
                    notchedRangeMin = parse.Float("m_flNotchedRangeMin");
                    notchedRangeMax = parse.Float("m_flNotchedRangeMax");
                    notchedOutputOutside = parse.Float("m_flNotchedOutputOutside");
                    notchedOutputInside = parse.Float("m_flNotchedOutputInside");
                    break;

                case PfMapType.Round:
                    roundType = parse.EnumNormalized<PfRoundType>("m_nRoundType", roundType);
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
                    var valueIn = inputMode switch
                    {
                        PfInputMode.Clamped => Math.Clamp(value, input0, input1),
                        PfInputMode.Looped => value % (input1 - input0),
                        _ => value
                    };

                    return MathUtils.RemapRange(valueIn, input0, input1, output0, output1);

                case PfMapType.RemapBiased:
                    var remappedTo0_1RangeBiased = MathUtils.Remap(value, input0, input1);

                    remappedTo0_1RangeBiased = inputMode == PfInputMode.Looped
                        ? MathUtils.Fract(remappedTo0_1RangeBiased)
                        : MathF.Min(remappedTo0_1RangeBiased, 1f);

                    var biased = NumericBias.FromBiasParameter(remappedTo0_1RangeBiased, biasParameter, biasType);

                    // The lower end of the input range is never clamped; the output range is what bounds
                    // the result, and the bias curve is free to run past either end on the way there
                    return Math.Clamp(
                        float.Lerp(output0, output1, biased),
                        MathF.Min(output0, output1),
                        MathF.Max(output0, output1));

                case PfMapType.Curve:
                    return curve!.Evaluate(value);

                case PfMapType.Notched:
                    return value >= notchedRangeMin && value <= notchedRangeMax
                        ? notchedOutputInside
                        : notchedOutputOutside;

                case PfMapType.Round:
                    return roundType switch
                    {
                        PfRoundType.Floor => MathF.Floor(value),
                        PfRoundType.Ceil => MathF.Ceiling(value),
                        _ => MathF.Round(value),
                    };

                default:
                    return value;
            }
        }
    }
}
