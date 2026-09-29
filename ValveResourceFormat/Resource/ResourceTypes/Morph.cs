using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a morph (flex) resource containing vertex deformation data.
    /// </summary>
    public class Morph : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the flex rules that define how controllers affect morphs.
        /// </summary>
        public FlexRule[] FlexRules { get; private set; } = [];

        /// <summary>
        /// Gets the flex controllers that drive morph animations.
        /// </summary>
        public FlexController[] FlexControllers { get; private set; } = [];

        /// <summary>
        /// Gets the texture containing encoded morph deltas.
        /// </summary>
        public Texture? Texture { get; private set; }

        /// <summary>
        /// Gets the resource containing the morph texture.
        /// </summary>
        public Resource? TextureResource { get; private set; }

        /// <summary>
        /// Gets the delta atlas this morph set names, whether or not it was found.
        /// </summary>
        public string AtlasPath => Data.GetStringProperty("m_pTextureAtlas", string.Empty);

        /// <summary>
        /// Gets whether the morph set names a delta atlas that <see cref="LoadFlexData"/> could not
        /// load. Every delta reads as zero while this holds.
        /// </summary>
        public bool HasMissingAtlas => loaded && Texture == null && AtlasPath.Length > 0;

        private bool loaded;

        /// <summary>
        /// Initializes a new instance of the <see cref="Morph"/> class.
        /// </summary>
        public Morph(BlockType type) : base(type, "MorphSetData_t")
        {
        }

        /// <summary>
        /// Gets the number of morphs.
        /// </summary>
        public int GetMorphCount()
        {
            var flexDesc = Data.GetArray("m_FlexDesc");
            return flexDesc.Count;
        }

        /// <summary>
        /// Gets the list of flex descriptors.
        /// </summary>
        public List<string> GetFlexDescriptors()
        {
            var flexDesc = Data.GetArray("m_FlexDesc");
            var result = new List<string>(flexDesc.Count);

            foreach (var f in flexDesc)
            {
                var name = f.GetStringProperty("m_szFacs");
                result.Add(name);
            }

            return result;
        }

        /// <summary>
        /// Gets the flex vertex data as a dictionary mapping flex names to vertex positions.
        /// </summary>
        public Dictionary<string, Vector3[]> GetFlexVertexData()
        {
            var bundle = GetFlexVertexData(MorphBundleType.PositionSpeed);
            var flexData = new Dictionary<string, Vector3[]>(bundle.Count);

            foreach (var (name, values) in bundle)
            {
                var positions = new Vector3[values.Length];

                for (var i = 0; i < values.Length; i++)
                {
                    positions[i] = new Vector3(values[i].X, values[i].Y, values[i].Z);
                }

                flexData.Add(name, positions);
            }

            return flexData;
        }

        /// <summary>Where a morph rect lands in the vertex grid, and its size in atlas pixels.</summary>
        private readonly record struct RectPlacement(int XLeftDst, int YTopDst, int Width, int Height);

        private static RectPlacement GetRectPlacement(KVObject rect, int texWidth, int texHeight)
        {
            return new RectPlacement(
                rect.GetInt32Property("m_nXLeftDst"),
                rect.GetInt32Property("m_nYTopDst"),
                (int)MathF.Round(rect.GetFloatProperty("m_flUWidthSrc") * texWidth, 0),
                (int)MathF.Round(rect.GetFloatProperty("m_flVHeightSrc") * texHeight, 0));
        }

        /// <summary>
        /// Gets the deltas of one bundle, keyed by flex name. The fourth component carries the speed of a
        /// position bundle or the wrinkle weight of a normal bundle.
        /// </summary>
        public Dictionary<string, Vector4[]> GetFlexVertexData(MorphBundleType bundleType)
        {
            var flexData = new Dictionary<string, Vector4[]>();

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
            if (morphDatas.Count == 0)
            {
                return flexData;
            }

            var bundleTypes = GetBundleTypes();
            flexData.EnsureCapacity(morphDatas.Count);

            foreach (var morphData in morphDatas)
            {
                if (morphData.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var morphName = morphData.GetStringProperty("m_name");
                if (string.IsNullOrEmpty(morphName))
                {
                    //Some empty names exist and may need to be skipped.
                    continue;
                }

                var rectData = new Vector4[height * width];
                rectData.Initialize();

                foreach (var rect in morphData.GetArray("m_morphRectDatas") ?? [])
                {
                    var placement = GetRectPlacement(rect, texWidth, texHeight);
                    var bundleDatas = rect.GetArray("m_bundleDatas") ?? [];

                    for (var bundleKey = 0; bundleKey < bundleDatas.Count; bundleKey++)
                    {
                        var bundleData = bundleDatas[bundleKey];

                        if (bundleTypes[bundleKey] != bundleType)
                        {
                            continue;
                        }

                        var rectU = (int)MathF.Round(bundleData.GetFloatProperty("m_flULeftSrc") * texWidth, 0);
                        var rectV = (int)MathF.Round(bundleData.GetFloatProperty("m_flVTopSrc") * texHeight, 0);
                        var ranges = new Vector4(bundleData.GetFloatArray("m_ranges"));
                        var offsets = new Vector4(bundleData.GetFloatArray("m_offsets"));

                        for (var row = rectV; row < rectV + placement.Height; row++)
                        {
                            for (var col = rectU; col < rectU + placement.Width; col++)
                            {
                                var colorIndex = row * texWidth + col;
                                var dstI = row - rectV + placement.YTopDst;
                                var dstJ = col - rectU + placement.XLeftDst;

                                // Older morph sets carry rects that run past the atlas or the vertex
                                // grid. A row that overruns the grid width is clipped, not wrapped
                                // onto the next row of vertices.
                                if (colorIndex < 0 || colorIndex >= texPixels.Length
                                    || dstI < 0 || dstI >= height || dstJ < 0 || dstJ >= width)
                                {
                                    continue;
                                }

                                var color = texPixels[colorIndex];

                                var vec = new Vector4(color.Red, color.Green, color.Blue, color.Alpha);
                                vec /= 255f;
                                vec *= ranges;
                                vec += offsets;

                                rectData[(dstI * width) + dstJ] = vec;
                            }
                        }
                    }
                }

                flexData.Add(morphName, rectData);
            }

            return flexData;
        }

        /// <summary>
        /// Gets the names of the flexes <see cref="GetFlexVertexData()"/> returns deltas for, without
        /// decoding the atlas.
        /// </summary>
        public HashSet<string> GetFlexNamesWithData()
        {
            var names = new HashSet<string>();

            if (Texture == null)
            {
                return names;
            }

            foreach (var morphData in GetMorphKeyValueCollection(Data, "m_morphDatas"))
            {
                if (morphData.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var morphName = morphData.GetStringProperty("m_name");
                if (!string.IsNullOrEmpty(morphName))
                {
                    names.Add(morphName);
                }
            }

            return names;
        }

        /// <summary>
        /// Gets which vertices each flex places a rect over, whether or not the delta there quantised to
        /// zero. A writer that only kept the non-zero deltas would hand the atlas packer a more
        /// fragmented shape than the one it started from.
        /// </summary>
        public Dictionary<string, bool[]> GetFlexVertexCoverage()
        {
            var coverage = new Dictionary<string, bool[]>();

            if (Texture == null)
            {
                return coverage;
            }

            var width = Data.GetInt32Property("m_nWidth");
            var height = Data.GetInt32Property("m_nHeight");
            var texWidth = Texture.Width;
            var texHeight = Texture.Height;

            foreach (var morphData in GetMorphKeyValueCollection(Data, "m_morphDatas"))
            {
                if (morphData.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var morphName = morphData.GetStringProperty("m_name");
                if (string.IsNullOrEmpty(morphName))
                {
                    continue;
                }

                var covered = new bool[height * width];

                foreach (var rect in morphData.GetArray("m_morphRectDatas") ?? [])
                {
                    var placement = GetRectPlacement(rect, texWidth, texHeight);

                    for (var row = 0; row < placement.Height; row++)
                    {
                        var dstI = row + placement.YTopDst;

                        if (dstI < 0 || dstI >= height)
                        {
                            continue;
                        }

                        for (var col = 0; col < placement.Width; col++)
                        {
                            var dstJ = col + placement.XLeftDst;

                            if (dstJ >= 0 && dstJ < width)
                            {
                                covered[(dstI * width) + dstJ] = true;
                            }
                        }
                    }
                }

                coverage[morphName] = covered;
            }

            return coverage;
        }

        /// <summary>
        /// Loads flex data from the file loader.
        /// </summary>
        public void LoadFlexData(IFileLoader fileLoader)
        {
            if (loaded)
            {
                return;
            }

            loaded = true;

            // The rig is described in this block, so it is readable whether or not an atlas of deltas
            // for it exists.
            FlexRules = GetMorphKeyValueCollection(Data, "m_FlexRules")
                .Select(kv => ParseFlexRule(kv))
                .ToArray();

            FlexControllers = GetMorphKeyValueCollection(Data, "m_FlexControllers")
                .Select(kv => ParseFlexController(kv))
                .ToArray();

            var atlasPath = Data.GetStringProperty("m_pTextureAtlas");
            if (string.IsNullOrEmpty(atlasPath))
            {
                return;
            }

            TextureResource = fileLoader.LoadFileCompiled(atlasPath);
            Texture = TextureResource?.DataBlock as Texture;
        }

        private static FlexController ParseFlexController(KVObject kv)
        {
            var name = kv.GetStringProperty("m_szName");
            var type = kv.GetStringProperty("m_szType");
            var min = kv.GetFloatProperty("min");
            var max = kv.GetFloatProperty("max");

            return new FlexController(name, type, min, max);
        }

        private static FlexRule ParseFlexRule(KVObject kv)
        {
            var flexId = kv.GetInt32Property("m_nFlex");

            var parsedOps = (kv.GetArray("m_FlexOps") ?? [])
                .Select(flexOp => ParseFlexOp(flexOp))
                .ToArray();

            // If there is an unimplemented flexop type in this rule, set the morph to zero instead to avoid exceptions.
            if (Array.IndexOf(parsedOps, null) >= 0)
            {
                return new FlexRule(flexId, [new FlexOpConst(0f)]);
            }

            return new FlexRule(flexId, Array.ConvertAll(parsedOps, op => op!));
        }

        private static FlexOp? ParseFlexOp(KVObject kv)
        {
            if (!kv.TryGetValue("m_OpCode", out var opCode))
            {
                return null;
            }

            var data = kv.GetInt32Property("m_Data");
            return FlexOp.Build(FlexOp.ParseOpCode(opCode), data);
        }

        private static MorphBundleType ParseBundleType(KVObject bundleType)
        {
            if (bundleType.ValueType is KVValueType.UInt32 or KVValueType.Int32 or KVValueType.UInt64 or KVValueType.Int64)
            {
                return (MorphBundleType)(int)bundleType;
            }

            if (bundleType.ValueType == KVValueType.String)
            {
                var bundleTypeString = (string)bundleType;
                return bundleTypeString switch
                {
                    "MORPH_BUNDLE_TYPE_NONE" or "BUNDLE_TYPE_NONE" => MorphBundleType.None,
                    "MORPH_BUNDLE_TYPE_POSITION_SPEED" or "BUNDLE_TYPE_POSITION_SPEED" => MorphBundleType.PositionSpeed,
                    "MORPH_BUNDLE_TYPE_NORMAL_WRINKLE" or "BUNDLE_TYPE_NORMAL_WRINKLE" => MorphBundleType.NormalWrinkle,
                    _ => throw new NotImplementedException($"Unhandled bundle type: {bundleTypeString}"),
                };
            }

            throw new NotImplementedException("Unhandled bundle type");
        }

        private static IReadOnlyList<KVObject> GetMorphKeyValueCollection(KVObject data, string name)
        {
            return data.GetArray(name) ?? [];
        }

        /// <summary>
        /// Gets what each bundle of a morph rect holds. The bundle types are shared by every rect, so this
        /// indexes into a rect's <c>m_bundleDatas</c>.
        /// </summary>
        public MorphBundleType[] GetBundleTypes()
        {
            return [.. GetMorphKeyValueCollection(Data, "m_bundleTypes").Select(ParseBundleType)];
        }

        /// <summary>
        /// Gets the morph data collection.
        /// </summary>
        public IReadOnlyList<KVObject> GetMorphDatas()
        {
            return GetMorphKeyValueCollection(Data, "m_morphDatas");
        }
    }
}
