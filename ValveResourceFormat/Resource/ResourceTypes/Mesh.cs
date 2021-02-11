using System;
using System.Numerics;
using System.Linq;
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
            VBIB = resource.VBIB;
            if (VBIB == null)
            {
                VBIB = VBIBFromDATA(GetData());
            }
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

        private static VBIB VBIBFromDATA(IKeyValueCollection data)
        {
            var VBIB = new VBIB();

            var vertexBuffers = data.GetArray("m_vertexBuffers");
            foreach (var vb in vertexBuffers)
            {
                VBIB.VertexBuffer vbOut = new VBIB.VertexBuffer();
                vbOut.Count = Convert.ToUInt32(vb.GetProperty<object>("m_nElementCount"));
                vbOut.Size = Convert.ToUInt32(vb.GetProperty<object>("m_nElementSizeInBytes"));
                vbOut.Attributes = new System.Collections.Generic.List<VBIB.VertexAttribute>();
                var inputLayoutFields = vb.GetArray("m_inputLayoutFields");
                foreach (var il in inputLayoutFields)
                {
                    VBIB.VertexAttribute attrib = new VBIB.VertexAttribute();
                    attrib.Name = System.Text.Encoding.UTF8.GetString(il.GetArray<object>("m_pSemanticName").
                        Select(Convert.ToByte).ToArray()).TrimEnd((char)0);
                    //"m_nSemanticIndex"
                    attrib.Type = (DXGI_FORMAT)Convert.ToUInt32(il.GetProperty<object>("m_Format"));
                    attrib.Offset = Convert.ToUInt32(il.GetProperty<object>("m_nOffset"));
                    //"m_nSlot"
                    //"m_nSlotType"
                    //"m_nInstanceStepRate"
                    vbOut.Attributes.Add(attrib);
                }
                vbOut.Buffer = vb.GetArray<object>("m_pData")
                    .Select(Convert.ToByte)
                    .ToArray();

                VBIB.VertexBuffers.Add(vbOut);
            }
            var indexBuffers = data.GetArray("m_indexBuffers");
            foreach (var ib in indexBuffers)
            {
                VBIB.IndexBuffer ibOut = new VBIB.IndexBuffer();
                ibOut.Count = Convert.ToUInt32(ib.GetProperty<object>("m_nElementCount"));
                ibOut.Size = Convert.ToUInt32(ib.GetProperty<object>("m_nElementSizeInBytes"));
                ibOut.Buffer = ib.GetArray<object>("m_pData")
                    .Select(Convert.ToByte)
                    .ToArray();
                VBIB.IndexBuffers.Add(ibOut);
            }

            return VBIB;
        }
    }
}
