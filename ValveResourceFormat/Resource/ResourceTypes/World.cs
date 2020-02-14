using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class World
    {
        private readonly Resource resource;

        public World(Resource resource)
        {
            this.resource = resource;
        }

        private IKeyValueCollection GetData()
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

            throw new InvalidOperationException($"Unknown world data type {data.GetType().Name}");
        }

        public IEnumerable<string> GetEntityLumpNames()
            => GetData().GetArray<string>("m_entityLumps");

        public IEnumerable<string> GetWorldNodeNames()
            => GetData().GetArray("m_worldNodes")
                .Select(nodeData => nodeData.GetProperty<string>("m_worldNodePrefix"))
                .ToList();
    }
}
