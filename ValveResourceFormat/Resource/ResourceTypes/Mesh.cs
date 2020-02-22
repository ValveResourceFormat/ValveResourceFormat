using System;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class Mesh
    {
        public ResourceData Data { get; }

        public VBIB VBIB { get; }

        public Mesh(Resource resource)
        {
            Data = resource.DataBlock;
            VBIB = resource.VBIB;
        }

        public Mesh(ResourceData data, VBIB vbib)
        {
            Data = data;
            VBIB = vbib;
        }

        public IKeyValueCollection GetData()
        {
            switch (Data)
            {
                case BinaryKV3 binaryKv: return binaryKv.Data;
                case NTRO ntro: return ntro.Output;
                default: throw new InvalidOperationException($"Unknown model data type {Data.GetType().Name}");
            }
        }
    }
}
