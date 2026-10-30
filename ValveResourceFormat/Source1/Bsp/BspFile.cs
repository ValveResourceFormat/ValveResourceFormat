using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ValveResourceFormat.Compression;

namespace ValveResourceFormat.Source1.Bsp
{
    internal sealed class BspFile
    {
        public const int HeaderLumpCount = 64;

        private const int Identifier = 0x50534256;
        private const int LzmaIdentifier = 0x414D5A4C;
        private const int LzmaHeaderSize = 17;
        private const int HeaderSize = 8 + (HeaderLumpCount * 16) + 4;

        private readonly byte[] data;
        private readonly LumpInfo[] lumps;
        private readonly Dictionary<LumpType, byte[]> decompressedLumps = [];

        private BspModel[]? models;
        private BspPlane[]? planes;
        private Vector3[]? vertices;
        private BspEdge[]? edges;
        private int[]? surfEdges;
        private BspFace[]? faces;
        private BspFace[]? facesHdr;
        private BspPrimitive[]? primitives;
        private ushort[]? primitiveIndices;
        private Vector3[]? primitiveVertices;
        private BspTextureInfo[]? textureInfos;
        private BspTextureData[]? textureData;
        private BspDispInfo[]? displacementInfos;
        private BspDispVert[]? displacementVerts;
        private BspBrush[]? brushes;
        private BspBrushSide[]? brushSides;
        private CubemapSample[]? cubemaps;
        private LeafAmbientIndex[]? leafAmbientIndices;
        private LeafAmbientIndex[]? leafAmbientIndicesHdr;
        private LeafAmbientLighting[]? leafAmbientLighting;
        private LeafAmbientLighting[]? leafAmbientLightingHdr;
        private string[]? textureNames;
        private List<BspEntity>? entities;
        private BspGameLump? gameLump;
        private BspStaticProps? staticProps;
        private BspVisibility? visibility;
        private DisplacementManager? displacementManager;
        private LightmapLayout? hdrLightmapLayout;
        private LightmapLayout? ldrLightmapLayout;
        private BspLeaf[]? leafs;
        private BspLeafWaterData[]? leafWaterData;
        private BspOverlayFade[]? overlayFades;
        private BspOverlay[]? overlays;
        private BspDetailProps? detailProps;

        public string Name { get; }
        public int Version { get; }
        public int MapRevision { get; }
        public bool IsL4D2HeaderLayout { get; }
        public IReadOnlyList<LumpInfo> Lumps => lumps;

        public BspFile(byte[] data, string name)
        {
            this.data = data;
            Name = name;

            if (data.Length < HeaderSize)
            {
                throw new InvalidDataException("File is too small to be a BSP.");
            }

            var span = data.AsSpan();

            if (BinaryPrimitives.ReadInt32LittleEndian(span) != Identifier)
            {
                throw new InvalidDataException("Not a VBSP file.");
            }

            Version = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
            MapRevision = BinaryPrimitives.ReadInt32LittleEndian(span[(HeaderSize - 4)..]);

            lumps = ReadLumpInfos(span, l4d2Layout: false);

            if (!AreLumpsInsideFile(lumps, data.Length))
            {
                var l4d2Lumps = ReadLumpInfos(span, l4d2Layout: true);

                if (!AreLumpsInsideFile(l4d2Lumps, data.Length))
                {
                    throw new InvalidDataException("Lump table is invalid for both header layouts.");
                }

                lumps = l4d2Lumps;
                IsL4D2HeaderLayout = true;
            }
        }

        public static BspFile Open(string path)
        {
            return new BspFile(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
        }

        public static BspFile Open(Stream stream, string name)
        {
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return new BspFile(memory.ToArray(), name);
        }

        private static LumpInfo[] ReadLumpInfos(ReadOnlySpan<byte> span, bool l4d2Layout)
        {
            var result = new LumpInfo[HeaderLumpCount];

            for (var i = 0; i < HeaderLumpCount; i++)
            {
                var entry = span.Slice(8 + (i * 16), 16);
                var a = BinaryPrimitives.ReadInt32LittleEndian(entry);
                var b = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
                var c = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
                var d = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);

                result[i] = l4d2Layout
                    ? new LumpInfo { Type = (LumpType)i, Version = a, Offset = b, Length = c, FourCC = d }
                    : new LumpInfo { Type = (LumpType)i, Offset = a, Length = b, Version = c, FourCC = d };
            }

            return result;
        }

        private static bool AreLumpsInsideFile(LumpInfo[] infos, int fileLength)
        {
            foreach (var info in infos)
            {
                if (info.Length == 0)
                {
                    continue;
                }

                if (info.Offset < HeaderSize || info.Length < 0 || (long)info.Offset + info.Length > fileLength)
                {
                    return false;
                }
            }

            return true;
        }

        public LumpInfo GetLumpInfo(LumpType type) => lumps[(int)type];

        public ReadOnlyMemory<byte> GetLumpData(LumpType type)
        {
            var info = lumps[(int)type];

            if (info.Length == 0)
            {
                return default;
            }

            var raw = data.AsMemory(info.Offset, info.Length);

            if (!IsLzmaCompressed(raw.Span))
            {
                return raw;
            }

            if (!decompressedLumps.TryGetValue(type, out var decompressed))
            {
                decompressed = DecompressLzma(raw.Span);
                decompressedLumps[type] = decompressed;
            }

            return decompressed;
        }

        internal ReadOnlyMemory<byte> GetFileData(long offset, int length)
        {
            return data.AsMemory((int)offset, length);
        }

        internal int FileLength => data.Length;

        internal static bool IsLzmaCompressed(ReadOnlySpan<byte> span)
        {
            return span.Length >= LzmaHeaderSize && BinaryPrimitives.ReadInt32LittleEndian(span) == LzmaIdentifier;
        }

        internal static byte[] DecompressLzma(ReadOnlySpan<byte> span)
        {
            var actualSize = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
            var lzmaSize = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
            var properties = span.Slice(12, LzmaDecoder.PropertiesSize);
            var input = span.Slice(LzmaHeaderSize, Math.Min(lzmaSize, span.Length - LzmaHeaderSize));

            var output = new byte[actualSize];
            LzmaDecoder.Decode(properties, input, output);
            return output;
        }

        private T[] ReadLump<T>(LumpType type) where T : unmanaged
        {
            var span = GetLumpData(type).Span;
            var size = Unsafe.SizeOf<T>();
            var count = span.Length / size;
            return MemoryMarshal.Cast<byte, T>(span[..(count * size)]).ToArray();
        }

        public BspModel[] Models => models ??= ReadLump<BspModel>(LumpType.Models);
        public BspPlane[] Planes => planes ??= ReadLump<BspPlane>(LumpType.Planes);
        public Vector3[] Vertices => vertices ??= ReadLump<Vector3>(LumpType.Vertexes);
        public BspEdge[] Edges => edges ??= ReadLump<BspEdge>(LumpType.Edges);
        public int[] SurfEdges => surfEdges ??= ReadLump<int>(LumpType.SurfEdges);
        public BspFace[] Faces => faces ??= ReadLump<BspFace>(LumpType.Faces);
        public BspFace[] FacesHdr => facesHdr ??= ReadLump<BspFace>(LumpType.FacesHdr);
        public BspPrimitive[] Primitives => primitives ??= ReadLump<BspPrimitive>(LumpType.Primitives);
        public ushort[] PrimitiveIndices => primitiveIndices ??= ReadLump<ushort>(LumpType.PrimIndices);
        public Vector3[] PrimitiveVertices => primitiveVertices ??= ReadLump<Vector3>(LumpType.PrimVerts);
        public BspTextureInfo[] TextureInfos => textureInfos ??= ReadLump<BspTextureInfo>(LumpType.TexInfo);
        public BspTextureData[] TextureData => textureData ??= ReadLump<BspTextureData>(LumpType.TexData);
        public BspDispInfo[] DisplacementInfos => displacementInfos ??= ReadLump<BspDispInfo>(LumpType.DispInfo);
        public BspDispVert[] DisplacementVerts => displacementVerts ??= ReadLump<BspDispVert>(LumpType.DispVerts);
        public BspBrush[] Brushes => brushes ??= ReadLump<BspBrush>(LumpType.Brushes);
        public BspBrushSide[] BrushSides => brushSides ??= ReadLump<BspBrushSide>(LumpType.BrushSides);
        public CubemapSample[] Cubemaps => cubemaps ??= ReadLump<CubemapSample>(LumpType.Cubemaps);
        public LeafAmbientIndex[] LeafAmbientIndices => leafAmbientIndices ??= ReadLeafAmbientIndices(LumpType.LeafAmbientIndex, LumpType.LeafAmbientLighting);
        public LeafAmbientIndex[] LeafAmbientIndicesHdr => leafAmbientIndicesHdr ??= ReadLeafAmbientIndices(LumpType.LeafAmbientIndexHdr, LumpType.LeafAmbientLightingHdr);
        public LeafAmbientLighting[] LeafAmbientLighting => leafAmbientLighting ??= ReadLeafAmbientLighting(LumpType.LeafAmbientIndex, LumpType.LeafAmbientLighting);
        public LeafAmbientLighting[] LeafAmbientLightingHdr => leafAmbientLightingHdr ??= ReadLeafAmbientLighting(LumpType.LeafAmbientIndexHdr, LumpType.LeafAmbientLightingHdr);

        // Maps from before the ambient index existed store one light cube per leaf instead of samples
        private bool HasLeafAmbientCubes(LumpType indexType, LumpType lightingType)
            => GetLumpData(indexType).Length == 0
            && GetLumpData(lightingType).Length == Leafs.Length * Unsafe.SizeOf<CompressedLightCube>();

        private LeafAmbientIndex[] ReadLeafAmbientIndices(LumpType indexType, LumpType lightingType)
        {
            if (!HasLeafAmbientCubes(indexType, lightingType))
            {
                return ReadLump<LeafAmbientIndex>(indexType);
            }

            var indices = new LeafAmbientIndex[Leafs.Length];

            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = new LeafAmbientIndex(1, (ushort)i);
            }

            return indices;
        }

        private LeafAmbientLighting[] ReadLeafAmbientLighting(LumpType indexType, LumpType lightingType)
        {
            if (!HasLeafAmbientCubes(indexType, lightingType))
            {
                return ReadLump<LeafAmbientLighting>(lightingType);
            }

            var cubes = ReadLump<CompressedLightCube>(lightingType);
            var samples = new LeafAmbientLighting[cubes.Length];

            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = new LeafAmbientLighting(cubes[i]);
            }

            return samples;
        }

        public ReadOnlyMemory<byte> Lighting => GetLumpData(LumpType.Lighting);
        public ReadOnlyMemory<byte> LightingHdr => GetLumpData(LumpType.LightingHdr);

        public BspLeafWaterData[] LeafWaterData => leafWaterData ??= ReadLump<BspLeafWaterData>(LumpType.LeafWaterData);
        public BspOverlayFade[] OverlayFades => overlayFades ??= ReadLump<BspOverlayFade>(LumpType.OverlayFades);

        /// <summary>Whether the map carries an HDR lightmap lump.</summary>
        public bool HasHdrLighting => LightingHdr.Length > 0;

        // Maps without an HDR face lump store the HDR lightmap offsets in the regular faces
        private BspFace[] HdrFaces => FacesHdr.Length > 0 ? FacesHdr : Faces;

        /// <summary>The faces that match the preferred lightmap lump (HDR when present).</summary>
        public BspFace[] LightmappedFaces => HasHdrLighting ? HdrFaces : Faces;

        /// <summary>The preferred lightmap sample lump (HDR when present).</summary>
        public ReadOnlyMemory<byte> LightmapSamples => HasHdrLighting ? LightingHdr : Lighting;

        /// <summary>Gets the face lump that goes with the chosen lightmap lump. Falls back to LDR when the map has no HDR lighting.</summary>
        public BspFace[] GetLightmappedFaces(bool preferHdr) => preferHdr && HasHdrLighting ? HdrFaces : Faces;

        /// <summary>Gets the lightmap sample lump of the chosen kind. Falls back to LDR when the map has no HDR lighting.</summary>
        public ReadOnlyMemory<byte> GetLightmapSamples(bool preferHdr) => preferHdr && HasHdrLighting ? LightingHdr : Lighting;

        /// <summary>Gets the lightmap layout for the HDR or LDR lump. Falls back to LDR when the map has no HDR lighting.</summary>
        public LightmapLayout GetLightmapLayout(bool preferHdr)
        {
            var hdr = preferHdr && HasHdrLighting;
            return hdr
                ? hdrLightmapLayout ??= new LightmapLayout(this, hdr: true)
                : ldrLightmapLayout ??= new LightmapLayout(this, hdr: false);
        }

        public string[] TextureNames => textureNames ??= ReadTextureNames();

        /// <summary>The BSP leaves. Only the fields shared by all lump versions are read.</summary>
        public BspLeaf[] Leafs => leafs ??= BspLeaf.Read(GetLumpInfo(LumpType.Leafs).Version, GetLumpData(LumpType.Leafs).Span);

        /// <summary>The map overlays followed by the water overlays.</summary>
        public IReadOnlyList<BspOverlay> Overlays => overlays ??= BspOverlay.Read(this);

        /// <summary>The detail props of the map (the dprp game lump).</summary>
        public BspDetailProps DetailProps => detailProps ??= new BspDetailProps(this);

        public IReadOnlyList<BspEntity> Entities => entities ??= BspEntity.Parse(GetLumpData(LumpType.Entities).Span);

        public BspGameLump GameLump => gameLump ??= new BspGameLump(this);
        public BspStaticProps StaticProps => staticProps ??= new BspStaticProps(this);
        public BspVisibility Visibility => visibility ??= new BspVisibility(GetLumpData(LumpType.Visibility));
        internal DisplacementManager DisplacementManager => displacementManager ??= new DisplacementManager(this);
        /// <summary>The lightmap layout of the preferred (HDR when present) lightmap lump.</summary>
        public LightmapLayout LightmapLayout => GetLightmapLayout(preferHdr: true);

        public Vector3 GetVertexFromSurfEdge(int surfEdgeIndex)
        {
            var surfEdge = SurfEdges[surfEdgeIndex];
            var edge = Edges[Math.Abs(surfEdge)];
            return Vertices[surfEdge >= 0 ? edge.A : edge.B];
        }

        public string GetTextureName(int texDataStringIndex)
        {
            return TextureNames[texDataStringIndex];
        }

        private string[] ReadTextureNames()
        {
            var table = ReadLump<int>(LumpType.TexDataStringTable);
            var stringData = GetLumpData(LumpType.TexDataStringData).Span;
            var names = new string[table.Length];

            for (var i = 0; i < table.Length; i++)
            {
                var start = table[i];

                if (start < 0 || start >= stringData.Length)
                {
                    names[i] = string.Empty;
                    continue;
                }

                var remaining = stringData[start..];
                var length = remaining.IndexOf((byte)0);

                if (length < 0)
                {
                    length = remaining.Length;
                }

                names[i] = Encoding.ASCII.GetString(remaining[..length]);
            }

            return names;
        }
    }
}
