using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Compression;

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
                var vertexBuffer = BufferDataFromDATA(vb);
                
                var decompressedSize = vertexBuffer.ElementCount * vertexBuffer.ElementSizeInBytes;
                if (vertexBuffer.Data.Length != decompressedSize)
                {
                    vertexBuffer.Data = MeshOptimizerVertexDecoder.DecodeVertexBuffer((int)vertexBuffer.ElementCount, (int)vertexBuffer.ElementSizeInBytes, vertexBuffer.Data);
                }
                VBIB.VertexBuffers.Add(vertexBuffer);
            }
            var indexBuffers = data.GetArray("m_indexBuffers");
            foreach (var ib in indexBuffers)
            {
                var indexBuffer = BufferDataFromDATA(ib);

                var decompressedSize = indexBuffer.ElementCount * indexBuffer.ElementSizeInBytes;
                if (indexBuffer.Data.Length != decompressedSize)
                {
                    indexBuffer.Data = MeshOptimizerIndexDecoder.DecodeIndexBuffer((int)indexBuffer.ElementCount, (int)indexBuffer.ElementSizeInBytes, indexBuffer.Data);
                }

                VBIB.IndexBuffers.Add(indexBuffer);
            }

            return VBIB;
        }

        private static VBIB.OnDiskBufferData BufferDataFromDATA(IKeyValueCollection data)
        {
            VBIB.OnDiskBufferData buffer = default(VBIB.OnDiskBufferData);
            buffer.ElementCount = Convert.ToUInt32(data.GetProperty<object>("m_nElementCount"));
            buffer.ElementSizeInBytes = Convert.ToUInt32(data.GetProperty<object>("m_nElementSizeInBytes"));
            buffer.InputLayoutFields = new List<VBIB.RenderInputLayoutField>();
            var inputLayoutFields = data.GetArray("m_inputLayoutFields");
            foreach (var il in inputLayoutFields)
            {
                VBIB.RenderInputLayoutField attrib = default(VBIB.RenderInputLayoutField);

                attrib.SemanticName = System.Text.Encoding.UTF8.GetString(il.GetArray<object>("m_pSemanticName").
                    Select(Convert.ToByte).ToArray()).TrimEnd((char)0);
                attrib.SemanticIndex = Convert.ToInt32(il.GetProperty<object>("m_nSemanticIndex"));
                attrib.Format = (DXGI_FORMAT)Convert.ToUInt32(il.GetProperty<object>("m_Format"));
                attrib.Offset = Convert.ToUInt32(il.GetProperty<object>("m_nOffset"));
                attrib.Slot = Convert.ToInt32(il.GetProperty<object>("m_nSlot"));
                attrib.SlotType = (RenderSlotType)Convert.ToUInt32(il.GetProperty<object>("m_nSlotType"));
                attrib.InstanceStepRate = Convert.ToInt32(il.GetProperty<object>("m_nInstanceStepRate"));

                buffer.InputLayoutFields.Add(attrib);
            }
            buffer.Data = data.GetArray<object>("m_pData")
                .Select(Convert.ToByte)
                .ToArray();

            return buffer;
        }
    }
}
