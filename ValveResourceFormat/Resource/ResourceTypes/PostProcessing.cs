using System.Diagnostics;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a post-processing resource.
    /// </summary>
    public class PostProcessing : KeyValuesOrNTRO
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PostProcessing"/> class.
        /// </summary>
        public PostProcessing() : base(BlockType.DATA, "PostProcessingResource_t")
        { }

        /// <summary>
        /// Gets the tonemap parameters.
        /// </summary>
        public KVObject? GetTonemapParams()
        {
            if (Data.GetProperty<bool>("m_bHasTonemapParams"))
            {
                return Data.GetProperty<KVObject>("m_toneMapParams");
            }

            return null;
        }

        /// <summary>
        /// Gets the bloom parameters.
        /// </summary>
        public KVObject? GetBloomParams()
        {
            if (Data.GetProperty<bool>("m_bHasBloomParams"))
            {
                return Data.GetProperty<KVObject>("m_bloomParams");
            }

            return null;
        }

        /// <summary>
        /// Gets the vignette parameters.
        /// </summary>
        public KVObject? GetVignetteParams()
        {
            if (Data.GetProperty<bool>("m_bHasVignetteParams"))
            {
                return Data.GetProperty<KVObject>("m_vignetteParams");
            }

            return null;
        }

        /// <summary>
        /// Gets the local contrast parameters.
        /// </summary>
        public KVObject? GetLocalContrastParams()
        {
            if (Data.GetProperty<bool>("m_bHasLocalContrastParams"))
            {
                return Data.GetProperty<KVObject>("m_localConstrastParams");
            }

            return null;
        }

        /// <summary>
        /// Determines whether color correction is enabled.
        /// </summary>
        public bool HasColorCorrection()
        {
            var value = Data["m_bHasColorCorrection"];

            if (value != null)
            {
                return (bool)value;
            }

            return true; // Assumed true pre Aperture Desk Job
        }

        /// <summary>
        /// Gets the color correction LUT dimension.
        /// </summary>
        public int GetColorCorrectionLUTDimension()
            => Data.GetInt32Property("m_nColorCorrectionVolumeDim");

        /// <summary>
        /// Gets the color correction LUT data.
        /// </summary>
        public byte[] GetColorCorrectionLUT()
            => Data.GetProperty<byte[]>("m_colorCorrectionVolumeData");

        /// <summary>
        /// Gets the RAW data format of the color correction LUT.
        /// </summary>
        public byte[] GetRAWData()
        {
            var lut = GetColorCorrectionLUT().Clone() as byte[];
            Debug.Assert(lut != null);

            var j = 0;
            for (var i = 0; i < lut.Length; i++)
            {
                // Skip each 4th byte, for RAW format
                if (((i + 1) % 4) == 0)
                {
                    continue;
                }

                lut[j++] = lut[i];
            }

            return lut[..j];
        }

        /// <summary>
        /// Converts this post-processing resource to Valve post-processing format.
        /// </summary>
        public string ToValvePostProcessing(bool preloadLookupTable = false, string lutFileName = "")
        {
            var outKV3 = new KVObject(null);
            outKV3.Add("_class", "CPostProcessData");

            var layers = KVObject.Array("m_layers");

            var tonemapParams = GetTonemapParams();
            var bloomParams = GetBloomParams();
            var vignetteParams = GetVignetteParams();
            var localContrastParams = GetLocalContrastParams();

            if (tonemapParams != null)
            {
                var tonemappingLayer = new KVObject(null);
                {
                    tonemappingLayer.Add("_class", "CToneMappingLayer");
                    tonemappingLayer.Add("m_name", "Tone Mapping");
                    tonemappingLayer.Add("m_nOpacityPercent", 100);
                    tonemappingLayer.Add("m_bVisible", true);
                    tonemappingLayer.Add("m_pLayerMask", default(KVValue));

                    var tonemappingLayerParams = new KVObject("m_params");
                    foreach (var kv in tonemapParams.Children)
                    {
                        tonemappingLayerParams.Add(kv.Name, kv.Value);
                    }

                    tonemappingLayer.Add(tonemappingLayerParams.Name, tonemappingLayerParams.Value);
                }

                layers.Add(tonemappingLayer.Value);
            }

            if (bloomParams != null)
            {
                var bloomLayer = new KVObject(null);
                {
                    bloomLayer.Add("_class", "CBloomLayer");
                    bloomLayer.Add("m_name", "Bloom");
                    bloomLayer.Add("m_nOpacityPercent", 100);
                    bloomLayer.Add("m_bVisible", true);
                    bloomLayer.Add("m_pLayerMask", default(KVValue));

                    var bloomLayerParams = new KVObject("m_params");
                    foreach (var kv in bloomParams.Children)
                    {
                        bloomLayerParams.Add(kv.Name, kv.Value);
                    }

                    bloomLayer.Add(bloomLayerParams.Name, bloomLayerParams.Value);
                }

                layers.Add(bloomLayer.Value);
            }

            if (vignetteParams != null)
            {
                // TODO: How does the vignette layer look like?
            }

            if (localContrastParams != null)
            {
                // TODO: How does the local contrast layer look like?
            }

            // All other layers are compiled into a 3D lookup table
            if (HasColorCorrection())
            {
                var ccLayer = new KVObject(null);
                {
                    ccLayer.Add("_class", "CColorLookupColorCorrectionLayer");
                    ccLayer.Add("m_name", "VRF Extracted Lookup Table");
                    ccLayer.Add("m_nOpacityPercent", 100);
                    ccLayer.Add("m_bVisible", true);
                    ccLayer.Add("m_pLayerMask", default(KVValue));
                    ccLayer.Add("m_fileName", lutFileName);

                    var lut = KVObject.Array("m_lut");

                    if (preloadLookupTable)
                    {
                        foreach (var b in GetRAWData())
                        {
                            lut.Add((KVValue)(b / 255d));
                        }
                    }

                    ccLayer.Add(lut.Name, lut.Value);
                    ccLayer.Add("m_nDim", GetColorCorrectionLUTDimension());
                }

                layers.Add(ccLayer.Value);
            }

            outKV3.Add(layers.Name, layers.Value);

            return new KV3File(outKV3).ToString();
        }
    }
}
