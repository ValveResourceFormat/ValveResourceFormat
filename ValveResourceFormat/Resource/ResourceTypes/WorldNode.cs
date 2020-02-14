using System;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class WorldNode
    {
        private readonly Resource resource;

        public WorldNode(Resource resource)
        {
            this.resource = resource;
        }

        public IKeyValueCollection GetData()
        {
            var data = resource.DataBlock;
            if (data is NTRO ntro)
            {
                return ntro.Output;
            }
            else if (data is BinaryKV3 kv)
            {
                return kv.Data;
            }

            throw new InvalidOperationException($"Unknown world node data type {data.GetType().Name}");
        }
    }
}
