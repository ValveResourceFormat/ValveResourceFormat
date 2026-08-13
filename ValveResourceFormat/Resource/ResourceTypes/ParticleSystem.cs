using System.Linq;
using System.Threading;
using ValveKeyValue;
using ValveKeyValue.KeyValues3;
using ValveResourceFormat.Particles.Upgrade;
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
        private readonly Lock upgradeLock = new();
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
        /// newest implemented format. Computed once and cached; the result is always a copy, so
        /// <see cref="KeyValuesOrNTRO.Data"/> is neither returned nor mutated.
        /// </summary>
        public KVObject GetUpgradedData()
        {
            lock (upgradeLock)
            {
                upgradedData ??= ParticleFormatUpgrader.UpgradeToLatest(Data, SourceFormat);
                return upgradedData;
            }
        }

        /// <summary>
        /// Gets the function entries traced through the conversion chain, reporting which entries
        /// survive, under which class name, and which the chain removes. The trace carries the
        /// upgraded tree it describes, so taking it before <see cref="GetUpgradedData"/> runs the
        /// chain once for both; taking it afterwards runs the chain a second time.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ParticleUpgradeTrace.TracedFunction>> GetUpgradeTrace()
        {
            lock (upgradeLock)
            {
                if (upgradeTrace == null)
                {
                    var traced = ParticleUpgradeTrace.TraceFunctions(Data, SourceFormat);
                    upgradeTrace = traced.Functions;
                    upgradedData ??= traced.UpgradedRoot;
                }

                return upgradeTrace;
            }
        }

        /// <summary>Builds a particle system from the provided keyvalues.</summary>
        /// <param name="data">The particle system definition.</param>
        /// <param name="format">The KV3 format the definition is authored in. Defaults to the
        /// newest format, which is what a definition built in memory against the current schema
        /// is; pass an older one only for data that really is authored in it.</param>
        public static ParticleSystem Create(KVObject data, KV3ID? format = null)
        {
            var system = new ParticleSystem
            {
                Resource = null!,
                Data = data,
            };

            system.createdFormat = format ?? ParticleFormatUpgrader.LatestFormat;

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
            => GetUpgradedData().GetArray("m_Operators") ?? Enumerable.Empty<KVObject>();

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
        /// Gets whether a child entry runs. <c>m_bDisableChild</c> only removes a child from
        /// behavior version 5 on; older definitions keep disabled children.
        /// </summary>
        /// <param name="childEntry">An entry of the <c>m_Children</c> array.</param>
        public bool IsChildEnabled(KVObject childEntry)
        {
            ArgumentNullException.ThrowIfNull(childEntry);

            return GetUpgradedData().GetInt32Property("m_nBehaviorVersion") < 5
                || !childEntry.GetBooleanProperty("m_bDisableChild");
        }

        /// <summary>
        /// Gets the names of child particles.
        /// </summary>
        /// <param name="enabledOnly">Whether to skip children disabled by <see cref="IsChildEnabled"/>.</param>
        public IEnumerable<string> GetChildParticleNames(bool enabledOnly = false)
        {
            IEnumerable<KVObject> children = GetUpgradedData().GetArray("m_Children");

            if (children == null)
            {
                return [];
            }

            if (enabledOnly)
            {
                children = children.Where(IsChildEnabled);
            }

            return children.Select(c => c.GetStringProperty("m_ChildRef")).ToList();
        }
    }
}
