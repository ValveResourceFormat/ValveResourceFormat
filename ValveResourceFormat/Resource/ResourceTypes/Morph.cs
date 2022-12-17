
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Serialization.NTRO;

namespace ValveResourceFormat.ResourceTypes
{
    public class Morph
    {
        private Dictionary<string, Dictionary<int, List<byte>>> _flexData;
        public Dictionary<string, Dictionary<int, List<byte>>> FlexData => _flexData;

        public IKeyValueCollection BundleTypes => _bundleTypes;
        private IKeyValueCollection _bundleTypes;

        public const int BytesPerVertex = 12;

        public Morph(Mesh mesh, IFileLoader fileLoader)
        {
            InitFlexData(mesh, fileLoader);
        }

        private static IKeyValueCollection GetMorphKeyValueCollection(IKeyValueCollection data, string name)
        {
            var kvObj = data.GetProperty<object>(name);
            IKeyValueCollection ikvc = null;
            if (kvObj is NTROStruct ntroStruct)
            {
                ikvc = ntroStruct.ToKVObject();
            }
            else if (kvObj is NTROValue[] ntroArray)
            {
                var kv = new KVObject("root", true);
                foreach (var ntro in ntroArray)
                {
                    kv.AddProperty("", ntro.ToKVValue());
                }
                ikvc = kv;
            }
            else
            {
                ikvc = kvObj as IKeyValueCollection;
            }

            return ikvc;
        }

        private void InitFlexData(Mesh mesh, IFileLoader fileLoader)
        {
            //Morph block is within vmdl.
            var mrphDataContainer = mesh.MorphData;
            IKeyValueCollection mrphData = null;
            if (mrphDataContainer == null)
            {
                //m_morphSet is within vmesh.
                var morphSetPath = mesh.GetData().GetStringProperty("m_morphSet") + "_c";
                if (!string.IsNullOrEmpty(morphSetPath))
                {
                    var morphSetResource = fileLoader.LoadFile(morphSetPath);
                    if (morphSetResource != null)
                    {
                        mrphData = morphSetResource.DataBlock.AsKeyValueCollection();
                    }
                }
            }
            else
            {
                mrphData = mrphDataContainer.Data;
            }

            if (mrphData == null)
            {
                return;
            }

            var atlasPath = mrphData.GetStringProperty("m_pTextureAtlas") + "_c";
            if (string.IsNullOrEmpty(atlasPath))
            {
                return;
            }
            var textureResource = fileLoader.LoadFile(atlasPath);
            if (textureResource == null)
            {
                return;
            }

            var width = mrphData.GetInt32Property("m_nWidth");
            var height = mrphData.GetInt32Property("m_nHeight");

            var texture = (Texture) textureResource.DataBlock;
            var texWidth = texture.Width;
            var texHeight = texture.Height;
            var skiaBitmap = texture.GenerateBitmap();
            var texPixels = skiaBitmap.Pixels;

            _flexData = new Dictionary<string, Dictionary<int, List<byte>>>();

            //Some vmorf_c may be another old struct(NTROValue, eg: models/heroes/faceless_void/faceless_void_body.vmdl_c).
            //the latest struct is IKeyValueCollection.
            var morphDatas = GetMorphKeyValueCollection(mrphData, "m_morphDatas");
            if (morphDatas == null || !morphDatas.Any())
            {
                return;
            }

            _bundleTypes = GetMorphKeyValueCollection(mrphData, "m_bundleTypes");
            var bundleTypeCount = _bundleTypes.Count();
            foreach (var pair in morphDatas)
            {
                var morphData = pair.Value as IKeyValueCollection;
                if (morphData == null)
                {
                    continue;
                }

                var morphName = morphData.GetProperty<string>("m_name");
                if (string.IsNullOrEmpty(morphName))
                {
                    //Exist some empty names may need skip.
                    continue;
                }
                Vector3[,,] rectData = new Vector3[bundleTypeCount, height, width];
                for (int c = 0; c < bundleTypeCount; c++)
                {
                    for (int i = 0; i < height; i++)
                    {
                        for (int j = 0; j < width; j++)
                        {
                            rectData[c,i,j] = Vector3.Zero;
                        }
                    }
                }

                var morphRectDatas = morphData.GetSubCollection("m_morphRectDatas");
                foreach (var morphRectData in morphRectDatas)
                {
                    var rect = morphRectData.Value as IKeyValueCollection;
                    var xLeftDst= rect.GetInt32Property("m_nXLeftDst");
                    var yTopDst = rect.GetInt32Property("m_nYTopDst");
                    var rectWidth = (int)Math.Round(rect.GetFloatProperty("m_flUWidthSrc") * texWidth, 0);
                    var rectHeight = (int)Math.Round(rect.GetFloatProperty("m_flVHeightSrc") * texHeight, 0);
                    var bundleDatas = rect.GetSubCollection("m_bundleDatas");
                    foreach (var bundleData in bundleDatas)
                    {
                        var bundleType = int.Parse(bundleData.Key);
                        var bundle = bundleData.Value as IKeyValueCollection;
                        var rectU = (int)Math.Round(bundle.GetFloatProperty("m_flULeftSrc") * texWidth, 0);
                        var rectV = (int)Math.Round(bundle.GetFloatProperty("m_flVTopSrc") * texHeight, 0);
                        var ranges = bundle.GetFloatArray("m_ranges");
                        var offsets = bundle.GetFloatArray("m_offsets");

                        for (var row = rectV; row < rectV + rectHeight; row++)
                        {
                            for (var col = rectU; col < rectU + rectWidth; col++)
                            {
                                var colorIndex = row * texWidth + col;
                                var color = texPixels[colorIndex];
                                var dstI = row - rectV + yTopDst;
                                var dstJ = col - rectU + xLeftDst;
                                rectData[bundleType, dstI, dstJ] = new Vector3(
                                    color.Red / 255f * ranges[0] + offsets[0],
                                    color.Green / 255f * ranges[1] + offsets[1],
                                    color.Blue / 255f * ranges[2] + offsets[2]
                                    );
                            }
                        }
                    }
                }

                var flexDataWithType = new Dictionary<int, List<byte>>();
                for (int c = 0; c < bundleTypeCount; c++)
                {
                    var tmp = new List<byte>(width * height * BytesPerVertex);
                    for (int i = 0; i < height; i++)
                    {
                        for (int j = 0; j < width; j++)
                        {
                            tmp.AddRange(BitConverter.GetBytes(rectData[c,i,j].X));
                            tmp.AddRange(BitConverter.GetBytes(rectData[c,i,j].Y));
                            tmp.AddRange(BitConverter.GetBytes(rectData[c,i,j].Z));
                        }
                    }
                    flexDataWithType.Add(c, tmp);
                }
                _flexData.Add(morphName, flexDataWithType);
            }
        }

        //TODO:Other enums are still unknown.
        public bool CheckBundleId(int bundleId)
        {
            var bundleType = _bundleTypes.GetProperty<object>(bundleId.ToString());
            if (bundleType is uint bundleTypeEnum)
            {
                if (bundleTypeEnum == (uint)MorphBundleType.MORPH_BUNDLE_TYPE_POSITION_SPEED)
                {
                    return true;
                }
            }
            else if (bundleType is string bundleTypeString)
            {
                if (bundleTypeString == MorphBundleType.MORPH_BUNDLE_TYPE_POSITION_SPEED.ToString())
                {
                    return true;
                }
            }
            return false;
        }
    }
}
