using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

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
        public KVObject GetTonemapParams()
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
        public KVObject GetBloomParams()
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
        public KVObject GetVignetteParams()
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
        public KVObject GetLocalContrastParams()
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
            if (Data.Properties.TryGetValue("m_bHasColorCorrection", out var value))
            {
                return (bool)value.Value;
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
            outKV3.AddProperty("_class", "CPostProcessData");

            var layers = new KVObject("m_layers", isArray: true);

            var tonemapParams = GetTonemapParams();
            var bloomParams = GetBloomParams();
            var vignetteParams = GetVignetteParams();
            var localContrastParams = GetLocalContrastParams();

            if (tonemapParams != null)
            {
                var tonemappingLayer = new KVObject(null);
                {
                    tonemappingLayer.AddProperty("_class", "CToneMappingLayer");
                    tonemappingLayer.AddProperty("m_name", "Tone Mapping");
                    tonemappingLayer.AddProperty("m_nOpacityPercent", 100);
                    tonemappingLayer.AddProperty("m_bVisible", true);
                    tonemappingLayer.AddProperty("m_pLayerMask", null);

                    var tonemappingLayerParams = new KVObject("m_params");
                    foreach (var kv in tonemapParams.Properties)
                    {
                        tonemappingLayerParams.AddProperty(kv.Key, kv.Value);
                    }

                    tonemappingLayer.AddProperty(tonemappingLayerParams.Key, tonemappingLayerParams);
                }

                layers.AddItem(tonemappingLayer);
            }

            if (bloomParams != null)
            {
                var bloomLayer = new KVObject(null);
                {
                    bloomLayer.AddProperty("_class", "CBloomLayer");
                    bloomLayer.AddProperty("m_name", "Bloom");
                    bloomLayer.AddProperty("m_nOpacityPercent", 100);
                    bloomLayer.AddProperty("m_bVisible", true);
                    bloomLayer.AddProperty("m_pLayerMask", null);

                    var bloomLayerParams = new KVObject("m_params");
                    foreach (var kv in bloomParams.Properties)
                    {
                        bloomLayerParams.AddProperty(kv.Key, kv.Value);
                    }

                    bloomLayer.AddProperty(bloomLayerParams.Key, bloomLayerParams);
                }

                layers.AddItem(bloomLayer);
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
                    ccLayer.AddProperty("_class", "CColorLookupColorCorrectionLayer");
                    ccLayer.AddProperty("m_name", "VRF Extracted Lookup Table");
                    ccLayer.AddProperty("m_nOpacityPercent", 100);
                    ccLayer.AddProperty("m_bVisible", true);
                    ccLayer.AddProperty("m_pLayerMask", null);
                    ccLayer.AddProperty("m_fileName", lutFileName);

                    var lut = new KVObject("m_lut", isArray: true);

                    if (preloadLookupTable)
                    {
                        foreach (var b in GetRAWData())
                        {
                            lut.AddItem(b / 255d);
                        }
                    }

                    ccLayer.AddProperty(lut.Key, lut);
                    ccLayer.AddProperty("m_nDim", GetColorCorrectionLUTDimension());
                }

                layers.AddItem(ccLayer);
            }

            outKV3.AddProperty(layers.Key, layers);

            return new KV3File(outKV3).ToString();
        }
    }
}
