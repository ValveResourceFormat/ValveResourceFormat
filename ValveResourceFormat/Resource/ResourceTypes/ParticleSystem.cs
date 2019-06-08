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

        public IKeyValueCollection GetBaseProperties()
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
            => GetBaseProperties().GetArray("m_Renderers");

        public IEnumerable<IKeyValueCollection> GetOperators()
            => GetBaseProperties().GetArray("m_Operators");

        public IEnumerable<IKeyValueCollection> GetInitializers()
            => GetBaseProperties().GetArray("m_Initializers");

        public IEnumerable<IKeyValueCollection> GetEmitters()
            => GetBaseProperties().GetArray("m_Emitters");
    }
}
