using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    public class ParticleSystem : KeyValuesOrNTRO
    {
        public IEnumerable<KVObject> GetRenderers()
            => Data.GetArray("m_Renderers") ?? Enumerable.Empty<KVObject>();

        public IEnumerable<KVObject> GetOperators()
            => Data.GetArray("m_Operators") ?? Enumerable.Empty<KVObject>();

        public IEnumerable<KVObject> GetForceGenerators()
            => Data.GetArray("m_ForceGenerators") ?? Enumerable.Empty<KVObject>();

        public IEnumerable<KVObject> GetInitializers()
            => Data.GetArray("m_Initializers") ?? Enumerable.Empty<KVObject>();

        public IEnumerable<KVObject> GetEmitters()
            => Data.GetArray("m_Emitters") ?? Enumerable.Empty<KVObject>();

        public IEnumerable<KVObject> GetPreEmissionOperators()
            => Data.GetArray("m_PreEmissionOperators") ?? Enumerable.Empty<KVObject>();

        public IEnumerable<string> GetChildParticleNames(bool enabledOnly = false)
        {
            IEnumerable<KVObject> children = Data.GetArray("m_Children");

            if (children == null)
            {
                return [];
            }

            if (enabledOnly)
            {
                children = children.Where(c => !c.ContainsKey("m_bDisableChild") || !c.GetProperty<bool>("m_bDisableChild"));
            }

            return children.Select(c => c.GetProperty<string>("m_ChildRef")).ToList();
        }
    }
}
