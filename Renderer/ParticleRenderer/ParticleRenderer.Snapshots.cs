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
using ValveResourceFormat.Renderer.Particles.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles
{

    /// <summary>Snapshots this system publishes to its control points.</summary>
    internal partial class ParticleRenderer
    {
        /// <summary>
        /// The snapshot bound to each control point of this system. Only the system's own
        /// <c>m_hSnapshot</c> or an injected runtime one lands here; a function reading a snapshot
        /// looks it up through <see cref="ParticleSystemRenderState"/>, which walks up to the parent.
        /// </summary>
        private readonly Dictionary<int, ParticleSnapshot> controlPointSnapshots = [];

        /// <summary>The control point this system publishes its own snapshot to.</summary>
        private readonly int snapshotControlPoint;

        /// <summary>
        /// Binds this system's snapshot to the control point it publishes on, preferring one handed in
        /// at construction (the cable node builds its rope points that way) over the authored
        /// <c>m_hSnapshot</c> resource, and returns that control point.
        /// </summary>
        private int PublishSnapshot(ParticleDefinitionParser parse, ParticleSnapshot? runtimeSnapshot)
        {
            var controlPoint = parse.Int32("m_nSnapshotControlPoint", 0);
            var snapshot = runtimeSnapshot ?? SnapshotBinding.LoadAuthored(parse, rendererContext.FileLoader);

            if (snapshot != null)
            {
                controlPointSnapshots[controlPoint] = snapshot;
            }

            return controlPoint;
        }

        /// <summary>
        /// Gets the particle snapshot associated with the given control point, or null if none exists.
        /// </summary>
        internal ParticleSnapshot? GetControlPointSnapshot(int controlPoint)
        {
            controlPointSnapshots.TryGetValue(controlPoint, out var snap);
            return snap;
        }
    }
}
