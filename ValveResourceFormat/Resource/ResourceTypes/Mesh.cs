using System.IO;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class Mesh : KeyValuesOrNTRO
    {
        public VBIB VBIB
        {
            get
            {
                //new format has VBIB block, for old format we can get it from NTRO DATA block
                cachedVBIB ??= Resource.VBIB ?? new VBIB(Data);
                return cachedVBIB;
            }
            set
            {
                cachedVBIB = value;
            }
        }

        public Vector3 MinBounds { get; private set; }
        public Vector3 MaxBounds { get; private set; }

        public Morph MorphData { get; set; }

        private VBIB cachedVBIB { get; set; }

        public Attachments Attachments { get; private set; }

        public Mesh(BlockType type) : base(type, "PermRenderMeshData_t")
        {
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);
            if (Data.ContainsKey("m_attachments"))
            {
                Attachments = Attachments.FromData(Data);
            }
        }

        public void GetBounds()
        {
            var sceneObjects = Data.GetArray("m_sceneObjects");
            if (sceneObjects.Length == 0)
            {
                MinBounds = MaxBounds = new Vector3(0, 0, 0);
                return;
            }

            var minBounds = sceneObjects[0].GetSubCollection("m_vMinBounds").ToVector3();
            var maxBounds = sceneObjects[0].GetSubCollection("m_vMaxBounds").ToVector3();

            for (var i = 1; i < sceneObjects.Length; ++i)
            {
                var localMin = sceneObjects[i].GetSubCollection("m_vMinBounds").ToVector3();
                var localMax = sceneObjects[i].GetSubCollection("m_vMaxBounds").ToVector3();

                minBounds.X = Math.Min(minBounds.X, localMin.X);
                minBounds.Y = Math.Min(minBounds.Y, localMin.Y);
                minBounds.Z = Math.Min(minBounds.Z, localMin.Z);
                maxBounds.X = Math.Max(maxBounds.X, localMax.X);
                maxBounds.Y = Math.Max(maxBounds.Y, localMax.Y);
                maxBounds.Z = Math.Max(maxBounds.Z, localMax.Z);
            }

            MinBounds = minBounds;
            MaxBounds = maxBounds;
        }

        public static bool IsCompressedNormalTangent(KVObject drawCall)
        {
            if (drawCall.ContainsKey("m_bUseCompressedNormalTangent"))
            {
                return drawCall.GetProperty<bool>("m_bUseCompressedNormalTangent");
            }

            if (!drawCall.ContainsKey("m_nFlags"))
            {
                return false;
            }

            var flags = drawCall.GetProperty<object>("m_nFlags");

            return flags switch
            {
                string flagsString => flagsString.Contains("MESH_DRAW_FLAGS_USE_COMPRESSED_NORMAL_TANGENT", StringComparison.InvariantCulture),
                long flagsLong => ((RenderMeshDrawPrimitiveFlags)flagsLong & RenderMeshDrawPrimitiveFlags.UseCompressedNormalTangent) != 0,
                byte flagsByte => ((RenderMeshDrawPrimitiveFlags)flagsByte & RenderMeshDrawPrimitiveFlags.UseCompressedNormalTangent) != 0,
                _ => false
            };
        }

        public static bool HasBakedLightingFromLightMap(KVObject drawCall)
            => drawCall.ContainsKey("m_bHasBakedLightingFromLightMap")
                && drawCall.GetProperty<bool>("m_bHasBakedLightingFromLightMap");

        public static bool HasBakedLightingFromVertexStream(KVObject drawCall)
            => drawCall.ContainsKey("m_bHasBakedLightingFromVertexStream")
                && drawCall.GetProperty<bool>("m_bHasBakedLightingFromVertexStream");

        public static bool IsOccluder(KVObject drawCall)
            => drawCall.ContainsKey("m_bIsOccluder")
                && drawCall.GetProperty<bool>("m_bIsOccluder");

        public void LoadExternalMorphData(IFileLoader fileLoader)
        {
            if (MorphData == null)
            {
                var morphSetPath = Data.GetStringProperty("m_morphSet");
                if (!string.IsNullOrEmpty(morphSetPath))
                {
                    var morphSetResource = fileLoader.LoadFileCompiled(morphSetPath);
                    if (morphSetResource != null)
                    {
                        MorphData = morphSetResource.GetBlockByType(BlockType.MRPH) as Morph;
                    }
                }
            }

            if (MorphData != null)
            {
                MorphData.LoadFlexData(fileLoader);

                //If texture was not loaded, that means that this model doesn't have any valid morph data.
                if (MorphData.TextureResource == null)
                {
                    MorphData = null;
                }
            }
        }
    }
}
