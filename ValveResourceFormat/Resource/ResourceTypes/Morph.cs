using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Serialization.NTRO;

namespace ValveResourceFormat.ResourceTypes
{
    public class Morph : KeyValuesOrNTRO
    {
        public Dictionary<string, Vector3[]> FlexData { get; private set; }

        public Morph(BlockType type) : base(type, "MorphSetData_t")
        {
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

        public void LoadFlexData(IFileLoader fileLoader)
        {
            var atlasPath = Data.GetStringProperty("m_pTextureAtlas");
            if (string.IsNullOrEmpty(atlasPath))
            {
                return;
            }

            var textureResource = fileLoader.LoadFile(atlasPath + "_c");
            if (textureResource == null)
            {
                return;
            }

            var width = Data.GetInt32Property("m_nWidth");
            var height = Data.GetInt32Property("m_nHeight");

            var texture = (Texture)textureResource.DataBlock;
            var texWidth = texture.Width;
            var texHeight = texture.Height;
            using var skiaBitmap = texture.GenerateBitmap();
            var texPixels = skiaBitmap.Pixels;

            FlexData = [];

            //Some vmorf_c may be another old struct(NTROValue, eg: models/heroes/faceless_void/faceless_void_body.vmdl_c).
            //the latest struct is IKeyValueCollection.
            var morphDatas = GetMorphKeyValueCollection(Data, "m_morphDatas");
            if (morphDatas == null || !morphDatas.Any())
            {
                return;
            }

            var bundleTypes = GetMorphKeyValueCollection(Data, "m_bundleTypes").Select(kv => ParseBundleType(kv.Value)).ToArray();

            foreach (var pair in morphDatas)
            {
                if (pair.Value is not IKeyValueCollection morphData)
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
                    var rect = morphRectData.Value as IKeyValueCollection;
                    var xLeftDst = rect.GetInt32Property("m_nXLeftDst");
                    var yTopDst = rect.GetInt32Property("m_nYTopDst");
                    var rectWidth = (int)Math.Round(rect.GetFloatProperty("m_flUWidthSrc") * texWidth, 0);
                    var rectHeight = (int)Math.Round(rect.GetFloatProperty("m_flVHeightSrc") * texHeight, 0);
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

                        var bundle = bundleData.Value as IKeyValueCollection;
                        var rectU = (int)Math.Round(bundle.GetFloatProperty("m_flULeftSrc") * texWidth, 0);
                        var rectV = (int)Math.Round(bundle.GetFloatProperty("m_flVTopSrc") * texHeight, 0);
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

                FlexData.Add(morphName, rectData);
            }
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

        private static IKeyValueCollection GetMorphKeyValueCollection(IKeyValueCollection data, string name)
        {
            var kvObj = data.GetProperty<object>(name);

            if (kvObj is NTROStruct ntroStruct)
            {
                return ntroStruct.ToKVObject();
            }

            if (kvObj is NTROValue[] ntroArray)
            {
                var kv = new KVObject("root", true);
                foreach (var ntro in ntroArray)
                {
                    kv.AddProperty("", ntro.ToKVValue());
                }
                return kv;
            }

            return kvObj as IKeyValueCollection;
        }

    }
}
