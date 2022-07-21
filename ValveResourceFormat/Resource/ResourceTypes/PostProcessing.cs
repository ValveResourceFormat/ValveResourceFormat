using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class PostProcessing : BinaryKV3
    {
        public IKeyValueCollection GetTonemapParams()
        {
            if (Data.GetProperty<bool>("m_bHasTonemapParams"))
            {
                return Data.GetProperty<IKeyValueCollection>("m_toneMapParams");
            }

            return null;
        }

        public IKeyValueCollection GetBloomParams()
        {
            if (Data.GetProperty<bool>("m_bHasBloomParams"))
            {
                return Data.GetProperty<IKeyValueCollection>("m_bloomParams");
            }

            return null;
        }

        public IKeyValueCollection GetVignetteParams()
        {
            if (Data.GetProperty<bool>("m_bHasVignetteParams"))
            {
                return Data.GetProperty<IKeyValueCollection>("m_vignetteParams");
            }

            return null;
        }

        public IKeyValueCollection GetLocalContrastParams()
        {
            if (Data.GetProperty<bool>("m_bHasLocalContrastParams"))
            {
                return Data.GetProperty<IKeyValueCollection>("m_localConstrastParams");
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

        public string ToValvePostProcessing()
        {
            var outKV3 = new KVObject(null);
            outKV3.AddProperty("_class", new KVValue(KVType.STRING, "CPostProcessData"));

            var layers = new KVObject("m_layers", isArray:true);

            var tonemapParams = GetTonemapParams();
            var bloomParams = GetBloomParams();
            var vignetteParams = GetVignetteParams();
            var localContrastParams = GetLocalContrastParams();

            if (tonemapParams != null)
            {
                var tonemappingLayer = new KVObject(null);
                {
                    tonemappingLayer.AddProperty("_class", new KVValue(KVType.STRING, "CToneMappingLayer"));
                    tonemappingLayer.AddProperty("m_name", new KVValue(KVType.STRING, "Tone Mapping"));
                    tonemappingLayer.AddProperty("m_nOpacityPercent", new KVValue(KVType.INT64, 100));
                    tonemappingLayer.AddProperty("m_bVisible", new KVValue(KVType.BOOLEAN, true));
                    tonemappingLayer.AddProperty("m_pLayerMask", new KVValue(KVType.NULL, null));

                    var tonemappingLayerParams = new KVObject("m_params");
                    foreach (var kv in ((KVObject)tonemapParams).Properties)
                    {
                        tonemappingLayerParams.AddProperty(kv.Key, kv.Value);
                    }

                    tonemappingLayer.AddProperty(tonemappingLayerParams.Key, new KVValue(KVType.OBJECT, tonemappingLayerParams));
                }

                layers.AddProperty("", new KVValue(KVType.OBJECT, tonemappingLayer));
            }

            if (bloomParams != null)
            {
                var bloomLayer = new KVObject(null);
                {
                    bloomLayer.AddProperty("_class", new KVValue(KVType.STRING, "CBloomLayer"));
                    bloomLayer.AddProperty("m_name", new KVValue(KVType.STRING, "Bloom"));
                    bloomLayer.AddProperty("m_nOpacityPercent", new KVValue(KVType.INT64, 100));
                    bloomLayer.AddProperty("m_bVisible", new KVValue(KVType.BOOLEAN, true));
                    bloomLayer.AddProperty("m_pLayerMask", new KVValue(KVType.NULL, null));

                    var bloomLayerParams = new KVObject("m_params");
                    foreach (var kv in ((KVObject)bloomParams).Properties)
                    {
                        bloomLayerParams.AddProperty(kv.Key, kv.Value);
                    }

                    bloomLayer.AddProperty(bloomLayerParams.Key, new KVValue(KVType.OBJECT, bloomLayerParams));
                }

                layers.AddProperty("", new KVValue(KVType.OBJECT, bloomLayer));
            }

            if (vignetteParams != null)
            {
                // TODO: How does the vignette layer look like?
            }

            if (localContrastParams != null)
            {
                // TODO: How does the local contrast layer look like?
            }

            if (HasColorCorrection())
            {
                // TODO: All other layers are converted into this. Extract to lookup table?
                // https://developer.valvesoftware.com/wiki/Color_Correction#RAW_File_Format
            }

            outKV3.AddProperty(layers.Key, new KVValue(KVType.ARRAY, layers));

            return new KV3File(outKV3).ToString();
        }
    }
}
