using System;
using System.Collections.Generic;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class ParticleSystem
    {
        private readonly Resource resource;

        public ParticleSystem(Resource resource)
        {
            this.resource = resource;
        }

        private IKeyValueCollection GetData()
        {
            var data = resource.Blocks[BlockType.DATA];
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

        public IEnumerable<IKeyValueCollection> GetRenderers()
            => GetData().GetArray("m_Renderers");

        public IEnumerable<IKeyValueCollection> GetOperators()
            => GetData().GetArray("m_Operators");

        public IEnumerable<IKeyValueCollection> GetInitializers()
            => GetData().GetArray("m_Initializers");

        public IEnumerable<IKeyValueCollection> GetEmitters()
            => GetData().GetArray("m_Emitters");
    }
}
