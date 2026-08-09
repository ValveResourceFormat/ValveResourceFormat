using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Particles.Constraints;
using ValveResourceFormat.Renderer.Particles.Emitters;
using ValveResourceFormat.Renderer.Particles.ForceGenerators;
using ValveResourceFormat.Renderer.Particles.Initializers;
using ValveResourceFormat.Renderer.Particles.Operators;
using ValveResourceFormat.Renderer.Particles.PreEmissionOperators;
using ValveResourceFormat.Renderer.Particles.Renderers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles
{

    /// <summary>Child systems: creation, group selection and group restarts.</summary>
    internal partial class ParticleRenderer
    {
        private void SnapshotChildControlPointOverrides(float frameTime)
        {
            foreach (var child in childParticleRenderers)
            {
                child.systemRenderState.SnapshotControlPointOverrideHistory(frameTime);
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

            foreach (var child in childParticleRenderers)
            {
                if (child.groupId == groupId)
                {
                    remaining++;
                }
            }

            var needed = Math.Clamp(numberOfChildren, 0, remaining);

            // Selection sampling: walk the group once, taking each candidate with probability
            // needed/remaining. Uniform over all subsets of that size, and needs no scratch buffer.
            foreach (var child in childParticleRenderers)
            {
                if (child.groupId != groupId)
                {
                    continue;
                }

                var chosen = needed > 0 && systemRenderState.Random.Next() * remaining < needed;
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
        internal void CollectChildStatesInGroup(int groupId, List<ParticleSystemRenderState> destination)
        {
            foreach (var child in childParticleRenderers)
            {
                if (child.groupId == groupId)
                {
                    destination.Add(child.systemRenderState);
                }
            }
        }

        /// <summary>
        /// Schedules every direct child tagged with <paramref name="groupId"/> to replay after
        /// <paramref name="duration"/>. This system and children in other groups keep running.
        /// </summary>
        internal void RestartChildrenInGroup(int groupId, float duration)
        {
            foreach (var child in childParticleRenderers)
            {
                if (child.groupId == groupId)
                {
                    child.systemRenderState.SetRestartTime(duration);
                }
            }
        }

        private void SetupChildParticles(IEnumerable<string> childNames)
        {
            foreach (var childName in childNames)
            {
                var childResource = rendererContext.FileLoader.LoadFileCompiled(childName);

                if (childResource == null)
                {
                    continue;
                }

                var childSystemDefinition = (ParticleSystem?)childResource.DataBlock;
                Debug.Assert(childSystemDefinition != null);

                var childSystem = new ParticleRenderer(childSystemDefinition, rendererContext, scene, null, systemRenderState)
                {
                    MainControlPoint = MainControlPoint
                };

                childParticleRenderers.Add(childSystem);
            }
        }
    }
}
