using System.Linq;
using ValveKeyValue;
using ValveKeyValue.KeyValues3;
using ValveResourceFormat.ResourceTypes.ParticleUpgrade;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a particle system resource. Every accessor reads the upgraded tree, so callers
    /// always see the newest class names and member spellings regardless of the stored format.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleSystemDefinition">CParticleSystemDefinition</seealso>
    public class ParticleSystem : KeyValuesOrNTRO
    {
        private KVObject? upgradedData;
        private IReadOnlyDictionary<string, IReadOnlyList<ParticleUpgradeTrace.TracedFunction>>? upgradeTrace;
        private KV3ID? createdFormat;

        /// <summary>
        /// The format the definition is stored in, or the one it was created as when it was built
        /// in memory rather than loaded from a resource.
        /// </summary>
        private KV3ID? SourceFormat => Format ?? createdFormat;

        /// <summary>
        /// Gets the particle system data upgraded through the vpcf format-conversion chain to the
        /// newest implemented format. Computed once and cached; <see cref="KeyValuesOrNTRO.Data"/>
        /// is never mutated.
        /// </summary>
        public KVObject GetUpgradedData()
        {
            upgradedData ??= ParticleFormatUpgrader.UpgradeToLatest(Data, SourceFormat);
            return upgradedData;
        }

        /// <summary>
        /// Gets the function entries traced through the conversion chain, reporting which entries
        /// survive, under which class name, and which the chain removes. Shares the upgrade with
        /// <see cref="GetUpgradedData"/>, so asking for both runs the chain once.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ParticleUpgradeTrace.TracedFunction>> GetUpgradeTrace()
        {
            if (upgradeTrace == null)
            {
                var traced = ParticleUpgradeTrace.TraceFunctions(Data, SourceFormat);
                upgradeTrace = traced.Functions;
                upgradedData ??= traced.UpgradedRoot;
            }

            return upgradeTrace;
        }

        /// <summary>Builds a particle system from the provided keyvalues.</summary>
        /// <param name="data">The particle system definition.</param>
        /// <param name="format">The KV3 format the definition is authored in. Pass the newest
        /// format for definitions built in memory so the conversion chain does not re-run on them.</param>
        public static ParticleSystem Create(KVObject data, KV3ID? format = null)
        {
            var system = new ParticleSystem
            {
                Resource = null!,
                Data = data,
            };

            system.createdFormat = format;

            return system;
        }

        /// <summary>
        /// Gets the renderers in the particle system.
        /// </summary>
        public IEnumerable<KVObject> GetRenderers()
            => GetUpgradedData().GetArray("m_Renderers") ?? Enumerable.Empty<KVObject>();

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
            => GetUpgradedData().GetArray("m_Initializers") ?? Enumerable.Empty<KVObject>();

        /// <summary>
        /// Gets the emitters in the particle system.
        /// </summary>
        public IEnumerable<KVObject> GetEmitters()
            => GetUpgradedData().GetArray("m_Emitters") ?? Enumerable.Empty<KVObject>();

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
            IEnumerable<KVObject> children = GetUpgradedData().GetArray("m_Children");

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
