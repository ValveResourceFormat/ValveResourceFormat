using System;
using System.Numerics;
using GUI.Utils;
using GUI.Types.ParticleRenderer.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    /// <summary>
    /// Particle attribute remapping handler.
    /// </summary>
    class AttributeMapping
    {
        public enum MapType
        {
            Direct,
            Multiply,
            Remap,
            RemapBiased,
            Curve,
            Invalid,
        };
        private readonly MapType mode;

        private readonly float multFactor;

        private readonly float input0;
        private readonly float input1;
        private readonly float output0;
        private readonly float output1;

        private readonly string biasType;
        private readonly float biasParameter;

        private readonly PiecewiseCurve curve;

        private readonly bool isLooped;


        public AttributeMapping(IKeyValueCollection parameters)
        {
            var mapMode = parameters.GetProperty<string>("m_nMapType");

            mode = mapMode switch
            {
                "PF_MAP_TYPE_DIRECT" => MapType.Direct,
                "PF_MAP_TYPE_MULT" => MapType.Multiply,
                "PF_MAP_TYPE_REMAP" => MapType.Remap,
                "PF_MAP_TYPE_REMAP_BIASED" => MapType.RemapBiased,
                "PF_MAP_TYPE_CURVE" => MapType.Curve,
                // "NOTCHED" EXISTS IN CS2
                _ => MapType.Invalid
            };

            switch (mode)
            {
                case MapType.Direct:
                    break;
                case MapType.Multiply:
                    multFactor = parameters.GetFloatProperty("m_flMultFactor");
                    break;
                case MapType.Remap:
                case MapType.RemapBiased:
                    input0 = parameters.GetFloatProperty("m_flInput0");
                    input1 = parameters.GetFloatProperty("m_flInput1");
                    output0 = parameters.GetFloatProperty("m_flOutput0");
                    output1 = parameters.GetFloatProperty("m_flOutput1");

                    isLooped = parameters.GetProperty<string>("m_nInputMode") == "PF_INPUT_MODE_LOOPED";
                    break;
                case MapType.Curve:
                    var curveData = parameters.GetSubCollection("m_Curve");
                    isLooped = parameters.GetProperty<string>("m_nInputMode") == "PF_INPUT_MODE_LOOPED";

                    curve = new PiecewiseCurve(curveData, isLooped);
                    break;
                default:
                    break;

            }

            if (mode == MapType.RemapBiased)
            {
                biasType = parameters.GetProperty<string>("m_nBiasType");
                biasParameter = parameters.GetFloatProperty("m_flBiasParameter");
            }
        }
        public float ApplyMapping(float value)
        {
            switch (mode)
            {
                case MapType.Multiply:
                    return value * multFactor;

                case MapType.Remap:
                    var remappedTo0_1Range = MathUtils.Remap(value, input0, input1);

                    if (isLooped) { remappedTo0_1Range = MathUtils.Fract(remappedTo0_1Range); }

                    return MathUtils.Lerp(remappedTo0_1Range, output0, output1);

                case MapType.RemapBiased:
                    var remappedTo0_1RangeBiased = MathUtils.Remap(value, input0, input1);

                    if (isLooped) { remappedTo0_1RangeBiased = MathUtils.Fract(remappedTo0_1RangeBiased); }

                    // Insert bias processing here. Shared with randombiased mode in INumberProvider

                    return MathUtils.Lerp(remappedTo0_1RangeBiased, output0, output1);

                case MapType.Curve:
                    return curve.Evaluate(value);

                default:
                    return value;
            }
        }
        /* NOTCHED PARAMS:
         * m_flNotchedRangeMin
         * m_flNotchedRangeMax
         * m_flNotchedOutputOutside
         * m_flNotchedOutputInside
         */
    }


}
