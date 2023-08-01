using GUI.Types.ParticleRenderer.Utils;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    /// <summary>
    /// Particle attribute remapping handler.
    /// </summary>
    class AttributeMapping
    {
        public enum PfInputMode
        {
            Invalid = -1,
            Clamped,
            Looped,
        }

        public enum PfMapType
        {
            Invalid = -1,
            Direct,
            Mult,
            Remap,
            RemapBiased,
            Curve,
            Notched,
        };

        private readonly PfMapType MapType;
        private readonly PfInputMode InputMode = PfInputMode.Clamped;

        private readonly float multFactor;

        private readonly float input0;
        private readonly float input1;
        private readonly float output0;
        private readonly float output1;

        private readonly PfBiasType biasType;
        private readonly float biasParameter;

        private readonly PiecewiseCurve curve;


        public AttributeMapping(IKeyValueCollection parameters)
        {
            var parse = new ParticleDefinitionParser(parameters);
            MapType = parse.EnumNormalized<PfMapType>("m_nMapType");
            InputMode = parse.EnumNormalized<PfInputMode>("m_nInputMode", InputMode);

            switch (MapType)
            {
                case PfMapType.Direct:
                    break;

                case PfMapType.Mult:
                    multFactor = parameters.GetFloatProperty("m_flMultFactor");
                    break;

                case PfMapType.Remap:
                case PfMapType.RemapBiased:
                    input0 = parameters.GetFloatProperty("m_flInput0");
                    input1 = parameters.GetFloatProperty("m_flInput1");
                    output0 = parameters.GetFloatProperty("m_flOutput0");
                    output1 = parameters.GetFloatProperty("m_flOutput1");
                    break;

                case PfMapType.Curve:
                    var curveData = parameters.GetSubCollection("m_Curve");
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

            if (MapType == PfMapType.RemapBiased)
            {
                biasType = parameters.GetEnumValue<PfBiasType>("m_nBiasType", normalize: true);
                biasParameter = parameters.GetFloatProperty("m_flBiasParameter");
            }
        }

        public float ApplyMapping(float value)
        {
            switch (MapType)
            {
                case PfMapType.Mult:
                    return value * multFactor;

                case PfMapType.Remap:
                    var remappedTo0_1Range = MathUtils.Remap(value, input0, input1);

                    if (InputMode == PfInputMode.Looped) { remappedTo0_1Range = MathUtils.Fract(remappedTo0_1Range); }

                    return MathUtils.Lerp(remappedTo0_1Range, output0, output1);

                case PfMapType.RemapBiased:
                    var remappedTo0_1RangeBiased = MathUtils.Remap(value, input0, input1);

                    if (InputMode == PfInputMode.Looped) { remappedTo0_1RangeBiased = MathUtils.Fract(remappedTo0_1RangeBiased); }

                    // Insert bias processing here. Shared with randombiased mode in INumberProvider

                    return MathUtils.Lerp(remappedTo0_1RangeBiased, output0, output1);

                case PfMapType.Curve:
                    return curve.Evaluate(value);

                default:
                    return value;
            }
        }
    }
}
