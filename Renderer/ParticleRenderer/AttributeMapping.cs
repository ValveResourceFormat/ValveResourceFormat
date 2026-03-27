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
        };

        private readonly PfMapType MapType;
        private readonly PfInputMode InputMode = PfInputMode.Clamped;

        private readonly float multFactor;

        private readonly float input0;
        private readonly float input1;
        private readonly float output0;
        private readonly float output1;

        //private readonly ParticleFloatBiasType biasType;
        //private readonly float biasParameter;

        private readonly PiecewiseCurve? curve;


        public AttributeMapping(ParticleDefinitionParser parse)
        {
            MapType = parse.EnumNormalized<PfMapType>("m_nMapType");
            InputMode = parse.EnumNormalized<PfInputMode>("m_nInputMode", InputMode);

            switch (MapType)
            {
                case PfMapType.Direct:
                    break;

                case PfMapType.Mult:
                    multFactor = parse.Float("m_flMultFactor");
                    break;

                case PfMapType.Remap:
                case PfMapType.RemapBiased:
                    input0 = parse.Float("m_flInput0");
                    input1 = parse.Float("m_flInput1");
                    output0 = parse.Float("m_flOutput0");
                    output1 = parse.Float("m_flOutput1");

                    MathUtils.MinMaxFixUp(ref input0, ref input1);
                    MathUtils.MinMaxFixUp(ref output0, ref output1);

                    break;

                case PfMapType.Curve:
                    var curveData = parse.Data.GetSubCollection("m_Curve");
                    curve = new PiecewiseCurve(curveData, InputMode == PfInputMode.Looped);
                    break;

                case PfMapType.Notched:
                    /* NOTCHED PARAMS:
                    * m_flNotchedRangeMin
                    * m_flNotchedRangeMax
                    * m_flNotchedOutputOutside
                    * m_flNotchedOutputInside
                    */
                    break;

                default:
                    break;

            }

#if false // TODO: implement
            if (MapType == PfMapType.RemapBiased)
            {
                biasType = parse.Enum<ParticleFloatBiasType>("m_nBiasType");
                biasParameter = parse.Float("m_flBiasParameter");
            }
#endif
        }

        public float ApplyMapping(float value)
        {
            switch (MapType)
            {
                case PfMapType.Mult:
                    return value * multFactor;

                case PfMapType.Remap:
                    var valueIn = InputMode switch
                    {
                        PfInputMode.Clamped => Math.Clamp(value, input0, input1),
                        PfInputMode.Looped => value % (input1 - input0),
                        _ => value
                    };

                    return MathUtils.RemapRange(valueIn, input0, input1, output0, output1);

                case PfMapType.RemapBiased:
                    var remappedTo0_1RangeBiased = MathUtils.Remap(value, input0, input1);

                    if (InputMode == PfInputMode.Looped) { remappedTo0_1RangeBiased = MathUtils.Fract(remappedTo0_1RangeBiased); }

                    // TODO: Insert bias processing here. Shared with randombiased mode in INumberProvider

                    return float.Lerp(output0, output1, remappedTo0_1RangeBiased);

                case PfMapType.Curve:
                    return curve!.Evaluate(value);

                default:
                    return value;
            }
        }
    }
}
