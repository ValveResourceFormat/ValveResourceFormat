using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class PostProcessing : KeyValuesOrNTRO
    {
        public PostProcessing() : base(BlockType.DATA, "PostProcessingResource_t")
        { }

        public KVObject GetTonemapParams()
        {
            if (Data.GetProperty<bool>("m_bHasTonemapParams"))
            {
                return Data.GetProperty<KVObject>("m_toneMapParams");
            }

            return null;
        }

        public KVObject GetBloomParams()
        {
            if (Data.GetProperty<bool>("m_bHasBloomParams"))
            {
                return Data.GetProperty<KVObject>("m_bloomParams");
            }

            return null;
        }

        public KVObject GetVignetteParams()
        {
            if (Data.GetProperty<bool>("m_bHasVignetteParams"))
            {
                return Data.GetProperty<KVObject>("m_vignetteParams");
            }

            return null;
        }

        public KVObject GetLocalContrastParams()
        {
            if (Data.GetProperty<bool>("m_bHasLocalContrastParams"))
            {
                return Data.GetProperty<KVObject>("m_localConstrastParams");
            }

            return null;
        }

        public bool HasColorCorrection()
        {
            if (Data.Properties.TryGetValue("m_bHasColorCorrection", out var value))
            {
                return (bool)value.Value;
            }

            return true; // Assumed true pre Aperture Desk Job
        }

        public int GetColorCorrectionLUTDimension()
            => Data.GetProperty<int>("m_nColorCorrectionVolumeDim");

        public byte[] GetColorCorrectionLUT()
            => Data.GetProperty<byte[]>("m_colorCorrectionVolumeData");

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
                    tonemappingLayer.AddProperty("m_pLayerMask", new KVValue(KVType.NULL, null));

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
                    bloomLayer.AddProperty("m_pLayerMask", new KVValue(KVType.NULL, null));

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
                    ccLayer.AddProperty("m_pLayerMask", new KVValue(KVType.NULL, null));
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
