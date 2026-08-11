using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a particle system resource.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleSystemDefinition">CParticleSystemDefinition</seealso>
    public class ParticleSystem : KeyValuesOrNTRO
    {
        /// <summary>Builds a particle system from the provided keyvalues.</summary>
        public static ParticleSystem Create(KVObject data)
            => new()
            {
                Resource = null!,
                Data = data,
            };

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
        /// Gets the constraints in the particle system. Constraints run after operators each frame and
        /// relax particle positions (e.g. distance/rope/plane/world-collision constraints).
        /// </summary>
        public IEnumerable<KVObject> GetConstraints()
            => Data.GetArray("m_Constraints") ?? Enumerable.Empty<KVObject>();

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
                children = children.Where(c => !c.GetBooleanProperty("m_bDisableChild"));
            }

            return children.Select(c => c.GetStringProperty("m_ChildRef")).ToList();
        }

        /// <summary>
        /// Gets the child particle entries, which carry the child's delay, endcap and detail level
        /// alongside its reference.
        /// </summary>
        public IEnumerable<KVObject> GetChildren() => Data.GetArray("m_Children") ?? [];
    }
}
