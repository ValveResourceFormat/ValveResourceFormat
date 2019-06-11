using System;
using System.Collections.Generic;
using System.Linq;
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
            => GetBaseProperties().GetArray("m_Renderers") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<IKeyValueCollection> GetOperators()
            => GetBaseProperties().GetArray("m_Operators") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<IKeyValueCollection> GetInitializers()
            => GetBaseProperties().GetArray("m_Initializers") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<IKeyValueCollection> GetEmitters()
            => GetBaseProperties().GetArray("m_Emitters") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<string> GetChildParticleNames(bool enabledOnly = false)
        {
            IEnumerable<IKeyValueCollection> children = GetBaseProperties().GetArray("m_Children");

            if (children == null)
            {
                return Enumerable.Empty<string>();
            }

            if (enabledOnly)
            {
                children = children.Where(c => !c.ContainsKey("m_bDisableChild") || !c.GetProperty<bool>("m_bDisableChild"));
            }

            return children.Select(c => c.GetProperty<string>("m_ChildRef")).ToList();
        }
    }
}
