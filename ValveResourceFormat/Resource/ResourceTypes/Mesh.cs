using System;
using System.Numerics;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class Mesh
    {
        public ResourceData Data { get; }

        public VBIB VBIB { get; }

        public Vector3 MinBounds { get; private set; }
        public Vector3 MaxBounds { get; private set; }

        public Mesh(Resource resource)
        {
            Data = resource.DataBlock;
            //new format has VBIB block, for old format we can get it from NTRO DATA block
            VBIB = resource.VBIB ?? new VBIB(GetData());
            GetBounds();
        }

        public Mesh(ResourceData data, VBIB vbib)
        {
            Data = data;
            VBIB = vbib;
            GetBounds();
        }

        public IKeyValueCollection GetData() => Data.AsKeyValueCollection();

        private void GetBounds()
        {
            var sceneObjects = GetData().GetArray("m_sceneObjects");
            if (sceneObjects.Length == 0)
            {
                MinBounds = MaxBounds = new Vector3(0, 0, 0);
                return;
            }

            var minBounds = sceneObjects[0].GetSubCollection("m_vMinBounds").ToVector3();
            var maxBounds = sceneObjects[0].GetSubCollection("m_vMaxBounds").ToVector3();

            for (int i = 1; i < sceneObjects.Length; ++i)
            {
                var localMin = sceneObjects[i].GetSubCollection("m_vMinBounds").ToVector3();
                var localMax = sceneObjects[i].GetSubCollection("m_vMaxBounds").ToVector3();

                minBounds.X = System.Math.Min(minBounds.X, localMin.X);
                minBounds.Y = System.Math.Min(minBounds.Y, localMin.Y);
                minBounds.Z = System.Math.Min(minBounds.Z, localMin.Z);
                maxBounds.X = System.Math.Max(maxBounds.X, localMax.X);
                maxBounds.Y = System.Math.Max(maxBounds.Y, localMax.Y);
                maxBounds.Z = System.Math.Max(maxBounds.Z, localMax.Z);
            }

            MinBounds = minBounds;
            MaxBounds = maxBounds;
        }

        public static bool IsCompressedNormalTangent(IKeyValueCollection drawCall)
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
                long flagsLong =>
                // TODO: enum
                (flagsLong & 2) == 2,
                byte flagsByte =>
                (flagsByte & 2) == 2,
                _ => false
            };
        }
    }
}
