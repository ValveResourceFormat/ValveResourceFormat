using System.Diagnostics;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles
{

    /// <summary>Child systems: creation, group selection and group restarts.</summary>
    public partial class ParticleSystemSimulation
    {
        /// <summary>Holds every direct child tagged with <paramref name="groupId"/> before its start until a selection releases it.</summary>
        internal void ParkChildrenInGroup(int groupId)
        {
            foreach (var child in childSimulations)
            {
                if (child.groupId != groupId)
                {
                    continue;
                }

                child.finishedBeforeStartAllowed = true;
                child.systemState.Age = float.MinValue;
            }
        }

        /// <summary>
        /// Restarts <paramref name="numberOfChildren"/> of the direct children tagged with
        /// <paramref name="groupId"/>, rounded up, picked at random, and stops the rest of the group with
        /// their particles removed. Children in other groups are untouched.
        /// </summary>
        internal void ChooseRandomChildrenInGroup(int groupId, float numberOfChildren)
        {
            ParkChildrenInGroup(groupId);

            var candidates = new List<ParticleSystemSimulation>();

            foreach (var child in childSimulations)
            {
                if (child.groupId == groupId && child.systemState.Age < 0f)
                {
                    candidates.Add(child);
                }
            }

            if (candidates.Count != 0 && numberOfChildren > 0f)
            {
                var picked = 0;

                do
                {
                    if (candidates.Count != 0)
                    {
                        var index = (int)(candidates.Count * systemState.Random.Next());
                        var child = candidates[index];

                        child.systemState.Age = 0f;
                        child.Restart();
                        candidates.RemoveAt(index);
                    }

                    picked++;
                }
                while (numberOfChildren > picked);
            }

            foreach (var child in childSimulations)
            {
                if (child.groupId == groupId && child.systemState.Age == float.MinValue)
                {
                    child.systemState.Age = 0f;
                    child.StopEmission(removeAllParticles: true, playEndCap: false);
                }
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

        /// <summary>Restarts every direct child tagged with <paramref name="groupId"/>. This system and children in other groups keep running.</summary>
        internal void RestartChildrenInGroup(int groupId)
        {
            foreach (var child in childSimulations)
            {
                if (child.groupId == groupId)
                {
                    child.Restart();
                }
            }
        }

        /// <summary>Whether <paramref name="state"/> belongs to a system tagged with <paramref name="groupId"/>.</summary>
        internal static bool IsInGroup(ParticleSystemState state, int groupId) => state.Data?.groupId == groupId;

        /// <summary>The group this system belongs to among its parent's children (<c>m_nGroupID</c>).</summary>
        internal int GroupId => groupId;

        /// <summary>
        /// Holds this child before its start until a group operator starts it again. With
        /// <paramref name="finishedWhileHeld"/> it also counts as finished while it waits.
        /// </summary>
        internal void Hold(bool finishedWhileHeld)
        {
            if (finishedWhileHeld)
            {
                finishedBeforeStartAllowed = true;
            }

            systemState.Age = float.MinValue;
        }

        private void SetupChildParticles(IReadOnlyList<KVObject> children)
        {
            for (var slot = 0; slot < children.Count; slot++)
            {
                var childInfo = children[slot];
                var parse = new ParticleDefinitionParser(childInfo, logger, BehaviorVersion);

                if (parse.Boolean("m_bDisableChild", false))
                {
                    continue;
                }

                var childName = childInfo.GetStringProperty("m_ChildRef");
                var childResource = fileLoader.LoadFileCompiled(childName);

                if (childResource == null)
                {
                    continue;
                }

                if (IsSelfOrAncestor(childResource.FileName))
                {
                    logger.LogUniqueWarning("Skipped child {Child} of particle system {File}, because it is that system or one of its parents", childName, Name);
                    continue;
                }

                var childSystemDefinition = (ParticleSystem?)childResource.DataBlock;
                Debug.Assert(childSystemDefinition != null);

                var childSystem = new ParticleSystemSimulation(childSystemDefinition, fileLoader, logger, null, systemState)
                {
                    startDelay = parse.Float("m_flDelay", 0f),
                    IsEndCapChild = parse.Boolean("m_bEndCap", false),
                    detailLevel = parse.Enum("m_nDetailLevel", ParticleDetailLevel.PARTICLEDETAIL_LOW)
                };

                childSystem.systemState.Age = childSystem.IsEndCapChild ? float.MinValue : -childSystem.startDelay;
                childSystem.childSlot = slot;

                childSimulations.Add(childSystem);
            }
        }

        /// <summary>Gives every system below this one the seed its place in the tree derives from this one's.</summary>
        private void DeriveChildSeeds()
        {
            foreach (var child in childSimulations)
            {
                child.systemState.Random.DeriveSeed(systemState.Random.Seed, child.childSlot);
                child.DeriveChildSeeds();
            }
        }

        private bool IsSelfOrAncestor(string? fileName)
        {
            for (var state = systemState; state != null; state = state.ParentSystem)
            {
                if (string.Equals(state.Data?.Name, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether this child is currently allowed to run under <paramref name="parentState"/>: it has
        /// started, and its detail tier is active.
        /// </summary>
        /// <param name="parentState">The parent system's state.</param>
        public bool ShouldRunAsChildOf(ParticleSystemState parentState)
            => detailLevel <= parentState.DetailLevel && systemState.Age >= 0f;
    }
}
