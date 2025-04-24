using System.Globalization;
using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class Morph : KeyValuesOrNTRO
    {
        public FlexRule[] FlexRules { get; private set; }
        public FlexController[] FlexControllers { get; private set; }
        public Texture Texture { get; private set; }
        public Resource TextureResource { get; private set; }

        public Morph(BlockType type) : base(type, "MorphSetData_t")
        {
        }

        public int GetMorphCount()
        {
            var flexDesc = Data.GetArray("m_FlexDesc");
            return flexDesc.Length;
        }

        public List<string> GetFlexDescriptors()
        {
            var flexDesc = Data.GetArray("m_FlexDesc");
            var result = new List<string>(flexDesc.Length);

            foreach (var f in flexDesc)
            {
                var name = f.GetStringProperty("m_szFacs");
                result.Add(name);
            }

            return result;
        }

        public Dictionary<string, Vector3[]> GetFlexVertexData()
        {
            var flexData = new Dictionary<string, Vector3[]>();

            if (Texture == null)
            {
                return flexData;
            }

            var width = Data.GetInt32Property("m_nWidth");
            var height = Data.GetInt32Property("m_nHeight");

            var texWidth = Texture.Width;
            var texHeight = Texture.Height;
            using var skiaBitmap = Texture.GenerateBitmap();
            var texPixels = skiaBitmap.Pixels;

            //Some vmorf_c may be another old struct(NTROValue, eg: models/heroes/faceless_void/faceless_void_body.vmdl_c).
            //the latest struct is KVObject.
            var morphDatas = GetMorphKeyValueCollection(Data, "m_morphDatas");
            if (morphDatas == null || morphDatas.Count == 0)
            {
                return flexData;
            }

            var bundleTypes = GetMorphKeyValueCollection(Data, "m_bundleTypes").Select(kv => ParseBundleType(kv.Value)).ToArray();
            flexData.EnsureCapacity(morphDatas.Count);

            foreach (var pair in morphDatas)
            {
                if (pair.Value is not KVObject morphData)
                {
                    continue;
                }

                var morphName = morphData.GetProperty<string>("m_name");
                if (string.IsNullOrEmpty(morphName))
                {
                    //Exist some empty names may need skip.
                    continue;
                }

                var rectData = new Vector3[height * width];
                rectData.Initialize();

                var morphRectDatas = morphData.GetSubCollection("m_morphRectDatas");
                foreach (var morphRectData in morphRectDatas)
                {
                    var rect = morphRectData.Value as KVObject;
                    var xLeftDst = rect.GetInt32Property("m_nXLeftDst");
                    var yTopDst = rect.GetInt32Property("m_nYTopDst");
                    var rectWidth = (int)MathF.Round(rect.GetFloatProperty("m_flUWidthSrc") * texWidth, 0);
                    var rectHeight = (int)MathF.Round(rect.GetFloatProperty("m_flVHeightSrc") * texHeight, 0);
                    var bundleDatas = rect.GetSubCollection("m_bundleDatas");

                    foreach (var bundleData in bundleDatas)
                    {
                        var bundleKey = int.Parse(bundleData.Key, CultureInfo.InvariantCulture);

                        // We currently only support Position.
                        // TODO: Add Normal support for gltf
                        if (bundleTypes[bundleKey] != MorphBundleType.PositionSpeed)
                        {
                            continue;
                        }

                        var bundle = bundleData.Value as KVObject;
                        var rectU = (int)MathF.Round(bundle.GetFloatProperty("m_flULeftSrc") * texWidth, 0);
                        var rectV = (int)MathF.Round(bundle.GetFloatProperty("m_flVTopSrc") * texHeight, 0);
                        var ranges = new Vector4(bundle.GetFloatArray("m_ranges"));
                        var offsets = new Vector4(bundle.GetFloatArray("m_offsets"));

                        for (var row = rectV; row < rectV + rectHeight; row++)
                        {
                            for (var col = rectU; col < rectU + rectWidth; col++)
                            {
                                var colorIndex = row * texWidth + col;
                                var color = texPixels[colorIndex];
                                var dstI = row - rectV + yTopDst;
                                var dstJ = col - rectU + xLeftDst;

                                var vec = new Vector4(color.Red, color.Green, color.Blue, color.Alpha);
                                vec /= 255f;
                                vec *= ranges;
                                vec += offsets;

                                rectData[dstI * width + dstJ] = new Vector3(vec.X, vec.Y, vec.Z); // We don't care about speed (alpha) yet
                            }
                        }
                    }
                }

                flexData.Add(morphName, rectData);
            }

            return flexData;
        }

        public void LoadFlexData(IFileLoader fileLoader)
        {
            var atlasPath = Data.GetStringProperty("m_pTextureAtlas");
            if (string.IsNullOrEmpty(atlasPath))
            {
                return;
            }

            TextureResource = fileLoader.LoadFileCompiled(atlasPath);
            if (TextureResource == null)
            {
                return;
            }

            Texture = (Texture)TextureResource.DataBlock;

            FlexRules = GetMorphKeyValueCollection(Data, "m_FlexRules")
                .Select(kv => ParseFlexRule(kv.Value))
                .ToArray();

            FlexControllers = GetMorphKeyValueCollection(Data, "m_FlexControllers")
                .Select(kv => ParseFlexController(kv.Value))
                .ToArray();
        }

        private static FlexController ParseFlexController(object obj)
        {
            if (obj is not KVObject kv)
            {
                throw new ArgumentException("Parameter is not KVObject");
            }

            var name = kv.GetStringProperty("m_szName");
            var type = kv.GetStringProperty("m_szType");
            var min = kv.GetFloatProperty("min");
            var max = kv.GetFloatProperty("max");

            return new FlexController(name, type, min, max);
        }

        private static FlexRule ParseFlexRule(object obj)
        {
            if (obj is not KVObject kv)
            {
                throw new ArgumentException("Parameter is not KVObject");
            }

            var flexId = kv.GetInt32Property("m_nFlex");

            var flexOps = kv.GetSubCollection("m_FlexOps")
                .Select(flexOp => ParseFlexOp(flexOp.Value))
                .ToArray();

            if (flexOps.Any(op => op == null))
            {
                //There's an unimplemented flexop type in this rule, let's make a flexrule that sets the morph to zero instead to avoid exceptions.
                flexOps = [new FlexOpConst(0f)];
            }

            return new FlexRule(flexId, flexOps);
        }

        private static FlexOp ParseFlexOp(object obj)
        {
            if (obj is not KVObject kv)
            {
                throw new ArgumentException("Parameter is not KVObject");
            }

            var opCode = kv.GetStringProperty("m_OpCode");
            var data = kv.GetInt32Property("m_Data");
            return FlexOp.Build(opCode, data);
        }

        private static MorphBundleType ParseBundleType(object bundleType)
        {
            if (bundleType is uint bundleTypeEnum)
            {
                return (MorphBundleType)bundleTypeEnum;
            }

            if (bundleType is string bundleTypeString)
            {
                return bundleTypeString switch
                {
                    "MORPH_BUNDLE_TYPE_POSITION_SPEED" => MorphBundleType.PositionSpeed,
                    "BUNDLE_TYPE_POSITION_SPEED" => MorphBundleType.PositionSpeed,
                    "MORPH_BUNDLE_TYPE_NORMAL_WRINKLE" => MorphBundleType.NormalWrinkle,
                    _ => throw new NotImplementedException($"Unhandled bundle type: {bundleTypeString}"),
                };
            }

            throw new NotImplementedException("Unhandled bundle type");
        }

        private static KVObject GetMorphKeyValueCollection(KVObject data, string name)
        {
            var kvObj = data.GetProperty<object>(name);
            return kvObj as KVObject;
        }

        public KVObject GetMorphDatas()
        {
            return GetMorphKeyValueCollection(Data, "m_morphDatas");
        }
    }
}
