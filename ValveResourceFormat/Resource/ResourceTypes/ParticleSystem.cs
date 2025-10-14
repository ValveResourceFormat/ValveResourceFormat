using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a particle system resource.
    /// </summary>
    public class ParticleSystem : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the renderers in the particle system.
        /// </summary>
        public IEnumerable<KVObject> GetRenderers()
            => Data.GetArray("m_Renderers") ?? Enumerable.Empty<KVObject>();

        /// <summary>
        /// Gets the operators in the particle system.
        /// </summary>
        public IEnumerable<KVObject> GetOperators()
            => Data.GetArray("m_Operators") ?? Enumerable.Empty<KVObject>();

        /// <summary>
        /// Gets the force generators in the particle system.
        /// </summary>
        public IEnumerable<KVObject> GetForceGenerators()
            => Data.GetArray("m_ForceGenerators") ?? Enumerable.Empty<KVObject>();

        /// <summary>
        /// Gets the initializers in the particle system.
        /// </summary>
        public IEnumerable<KVObject> GetInitializers()
            => Data.GetArray("m_Initializers") ?? Enumerable.Empty<KVObject>();

        /// <summary>
        /// Gets the emitters in the particle system.
        /// </summary>
        public IEnumerable<KVObject> GetEmitters()
            => Data.GetArray("m_Emitters") ?? Enumerable.Empty<KVObject>();

        /// <summary>
        /// Gets the pre-emission operators in the particle system.
        /// </summary>
        public IEnumerable<KVObject> GetPreEmissionOperators()
            => Data.GetArray("m_PreEmissionOperators") ?? Enumerable.Empty<KVObject>();

        /// <summary>
        /// Gets the names of child particles.
        /// </summary>
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
