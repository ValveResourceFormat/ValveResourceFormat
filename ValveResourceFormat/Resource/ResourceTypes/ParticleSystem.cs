using System.Linq;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class ParticleSystem : KeyValuesOrNTRO
    {
        public IEnumerable<IKeyValueCollection> GetRenderers()
            => Data.GetArray("m_Renderers") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<IKeyValueCollection> GetOperators()
            => Data.GetArray("m_Operators") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<IKeyValueCollection> GetInitializers()
            => Data.GetArray("m_Initializers") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<IKeyValueCollection> GetEmitters()
            => Data.GetArray("m_Emitters") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<IKeyValueCollection> GetPreEmissionOperators()
            => Data.GetArray("m_PreEmissionOperators") ?? Enumerable.Empty<IKeyValueCollection>();

        public IEnumerable<string> GetChildParticleNames(bool enabledOnly = false)
        {
            IEnumerable<IKeyValueCollection> children = Data.GetArray("m_Children");

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
