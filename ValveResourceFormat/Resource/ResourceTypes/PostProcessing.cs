using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class PostProcessing : BinaryKV3
    {
        public string ToValvePostProcessing()
        {
            var outKV3 = new KVObject(null);
            outKV3.AddProperty("_class", new KVValue(KVType.STRING, "CPostProcessData"));

            var layers = new KVObject("m_layers", isArray:true);

            if (Data.GetProperty<bool>("m_bHasTonemapParams"))
            {
                var tonemappingLayer = new KVObject(null);
                {
                    tonemappingLayer.AddProperty("_class", new KVValue(KVType.STRING, "CToneMappingLayer"));
                    tonemappingLayer.AddProperty("m_name", new KVValue(KVType.STRING, "Tone Mapping"));
                    tonemappingLayer.AddProperty("m_nOpacityPercent", new KVValue(KVType.INT64, 100));
                    tonemappingLayer.AddProperty("m_bVisible", new KVValue(KVType.BOOLEAN, true));
                    tonemappingLayer.AddProperty("m_pLayerMask", new KVValue(KVType.NULL, null));

                    var tonemappingLayerParams = new KVObject("m_params");
                    foreach (var kv in Data.GetProperty<KVObject>("m_toneMapParams").Properties)
                    {
                        tonemappingLayerParams.AddProperty(kv.Key, kv.Value);
                    }

                    tonemappingLayer.AddProperty(tonemappingLayerParams.Key, new KVValue(KVType.OBJECT, tonemappingLayerParams));
                }

                layers.AddProperty("", new KVValue(KVType.OBJECT, tonemappingLayer));
            }

            if (Data.GetProperty<bool>("m_bHasBloomParams"))
            {
                var bloomLayer = new KVObject(null);
                {
                    bloomLayer.AddProperty("_class", new KVValue(KVType.STRING, "CBloomLayer"));
                    bloomLayer.AddProperty("m_name", new KVValue(KVType.STRING, "Bloom"));
                    bloomLayer.AddProperty("m_nOpacityPercent", new KVValue(KVType.INT64, 100));
                    bloomLayer.AddProperty("m_bVisible", new KVValue(KVType.BOOLEAN, true));
                    bloomLayer.AddProperty("m_pLayerMask", new KVValue(KVType.NULL, null));

                    var bloomLayerParams = new KVObject("m_params");
                    foreach (var kv in Data.GetProperty<KVObject>("m_bloomParams").Properties)
                    {
                        bloomLayerParams.AddProperty(kv.Key, kv.Value);
                    }

                    bloomLayer.AddProperty(bloomLayerParams.Key, new KVValue(KVType.OBJECT, bloomLayerParams));
                }

                layers.AddProperty("", new KVValue(KVType.OBJECT, bloomLayer));
            }

            if (Data.GetProperty<bool>("m_bHasVignetteParams"))
            {
                // TODO: How does the vignette layer look like?
            }

            if (Data.ContainsKey("m_colorCorrectionVolumeData"))
            {
                // TODO: All other layers are converted into this. Extract to lookup table?
                // var colorCorrectionVolumeDim = Data.GetProperty<int>("m_nColorCorrectionVolumeDim");
                // var colorCorrectionVolumeData = Data.GetProperty<byte[]>("m_colorCorrectionVolumeData");
            }

            outKV3.AddProperty(layers.Key, new KVValue(KVType.ARRAY, layers));

            return new KV3File(outKV3).ToString();
        }
    }
}
