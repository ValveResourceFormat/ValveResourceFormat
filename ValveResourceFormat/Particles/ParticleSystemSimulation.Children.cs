using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Constraints;
using ValveResourceFormat.Particles.Emitters;
using ValveResourceFormat.Particles.ForceGenerators;
using ValveResourceFormat.Particles.Initializers;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Particles.PreEmissionOperators;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles
{

    /// <summary>Child systems: creation, group selection and group restarts.</summary>
    public partial class ParticleSystemSimulation
    {
        private void SnapshotChildControlPointOverrides(float frameTime)
        {
            foreach (var child in childSimulations)
            {
                child.systemState.SnapshotControlPointOverrideHistory(frameTime);
                child.SnapshotChildControlPointOverrides(frameTime);
            }
        }

        /// <summary>
        /// Suppresses every child system tagged with <paramref name="groupId"/> except for
        /// <paramref name="numberOfChildren"/> randomly chosen ones. Children in other groups are untouched.
        /// </summary>
        internal void ChooseRandomChildrenInGroup(int groupId, int numberOfChildren)
        {
            var remaining = 0;

            foreach (var child in childSimulations)
            {
                if (child.groupId == groupId)
                {
                    remaining++;
                }
            }

            var needed = Math.Clamp(numberOfChildren, 0, remaining);

            // Selection sampling: walk the group once, taking each candidate with probability
            // needed/remaining. Uniform over all subsets of that size, and needs no scratch buffer.
            foreach (var child in childSimulations)
            {
                if (child.groupId != groupId)
                {
                    continue;
                }

                var chosen = needed > 0 && systemState.Random.Next() * remaining < needed;
                child.childEnabled = chosen;

                if (chosen)
                {
                    needed--;
                }

                remaining--;
            }
        }

        /// <summary>
        /// Appends the render states of the direct children tagged with <paramref name="groupId"/> to
        /// <paramref name="destination"/>, in definition order.
        /// </summary>
        internal void CollectChildStatesInGroup(int groupId, List<ParticleSystemState> destination)
        {
            foreach (var child in childSimulations)
            {
                if (child.groupId == groupId)
                {
                    destination.Add(child.systemState);
                }
            }
        }

        /// <summary>
        /// Schedules every direct child tagged with <paramref name="groupId"/> to replay after
        /// <paramref name="duration"/>. This system and children in other groups keep running.
        /// </summary>
        internal void RestartChildrenInGroup(int groupId, float duration)
        {
            foreach (var child in childSimulations)
            {
                if (child.groupId == groupId)
                {
                    child.systemState.SetRestartTime(duration);
                }
            }
        }

        private void SetupChildParticles(IEnumerable<KVObject> children)
        {
            foreach (var childInfo in children)
            {
                var parse = new ParticleDefinitionParser(childInfo, logger, BehaviorVersion);

                if (BehaviorVersion >= 5 && parse.Boolean("m_bDisableChild", false))
                {
                    continue;
                }

                var childName = childInfo.GetStringProperty("m_ChildRef");
                var childResource = fileLoader.LoadFileCompiled(childName);

                if (childResource == null)
                {
                    continue;
                }

                var childSystemDefinition = (ParticleSystem?)childResource.DataBlock;
                Debug.Assert(childSystemDefinition != null);

                var childSystem = new ParticleSystemSimulation(childSystemDefinition, fileLoader, logger, null, systemState)
                {
                    MainControlPoint = MainControlPoint
                };

                childSystem.startDelay = parse.Float("m_flDelay", 0f);
                childSystem.isEndCapChild = parse.Boolean("m_bEndCap", false);
                childSystem.detailLevel = parse.Enum("m_nDetailLevel", ParticleDetailLevel.PARTICLEDETAIL_LOW);

                childSimulations.Add(childSystem);
            }
        }

        /// <summary>
        /// Whether the parent should advance this child this frame. An endcap child lies dormant until
        /// the parent starts its endcap, a delayed child waits out its delay on the parent's clock, and a
        /// child authored above the active detail tier never runs at all.
        /// </summary>
        /// <summary>
        /// Whether this child is currently allowed to run under <paramref name="parentState"/>.
        /// </summary>
        /// <param name="parentState">The parent system's state.</param>
        public bool ShouldRunAsChildOf(ParticleSystemState parentState)
        {
            if (!childEnabled || detailLevel > parentState.DetailLevel)
            {
                return false;
            }

            if (isEndCapChild)
            {
                return parentState.InEndCap;
            }

            return parentState.Age >= startDelay;
        }
    }
}
