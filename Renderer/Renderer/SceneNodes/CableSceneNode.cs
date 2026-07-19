using System.Collections;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Parsed cable geometry: the spline nodes plus per-node attributes resolved from the entity.
    /// Consumed by <see cref="CableMeshBuilder"/>.
    /// </summary>
    readonly struct CableGeometry
    {
        public required IReadOnlyList<PathParticleRopeNode> Nodes { get; init; }
        public required float[] RadiusScales { get; init; }
        public required Vector3[] Colors { get; init; }
        public required bool[] Pins { get; init; }
        public float BaseRadius { get; init; }
        public float ParticleSpacing { get; init; }

        public readonly float RadiusAt(int nodeIndex)
        {
            var scale = nodeIndex >= 0 && nodeIndex < RadiusScales.Length ? RadiusScales[nodeIndex] : 1.0f;
            return BaseRadius * scale;
        }

        // Per-node colour defaults to white; the whole-cable color_tint is folded in later by
        // BuildSnapshot, so it must not be baked in here too (that would tint twice). The per-particle colour
        // is pathnodecolor and color_tint rides on top.
        public readonly Vector3 ColorAt(int nodeIndex)
            => nodeIndex >= 0 && nodeIndex < Colors.Length ? Colors[nodeIndex] : Vector3.One;

        // A path node anchors the rope (force_scale 0). Default: every node is pinned; an explicit
        // pin-enabled blob overrides per node.
        public readonly bool IsNodePinned(int nodeIndex)
            => nodeIndex < 0 || nodeIndex >= Pins.Length || Pins[nodeIndex];
    }

    /// <summary>
    /// Builds the scene node for a <c>path_particle_rope</c> / <c>path_particle_rope_clientside</c> map
    /// entity: the stored spline is sampled into rope particles seeded through a runtime snapshot, and the
    /// real particle system (<c>C_OP_BasicMovement</c> gravity + <c>C_OP_RopeSpringConstraint</c>, pinned at
    /// the path nodes) settles them into the slack droop, drawn as a tube by the <c>C_OP_RenderCables</c>
    /// renderer.
    /// </summary>
    static class CableSceneNode
    {
        // Authoritative editor defaults from PathParticleRopeBase / path_node_cable in base.fgd,
        // used only when an entity omits the key (compiled maps usually store them explicitly).
        private const float DefaultRadius = 4.0f;
        private const float DefaultSlack = 0.5f;
        private const float DefaultParticleSpacing = 32.0f;
        private const string DefaultEffectName = "particles/entity/path_particle_cable_default.vpcf";

        /// <summary>
        /// Attempts to build a cable node for a <c>path_particle_rope</c> entity. Returns false (and a null
        /// node) for degenerate input (empty/single-node paths, no usable effect, non-positive authored
        /// radius or spacing), in which case the entity should be ignored.
        /// </summary>
        public static bool TryCreate(Scene scene, Entity entity, Matrix4x4 parentTransform, out ParticleSceneNode? node)
        {
            node = null;

            var nodes = PathParticleRope.ParseNodes(entity.GetStringProperty("pathnodes"));
            if (nodes.Count < 2)
            {
                return false;
            }

            var (particleSystem, maxParticles) = LoadEffect(scene, entity);
            if (particleSystem == null)
            {
                return false;
            }

            var baseRadius = entity.ContainsKey("radius") ? entity.GetFloatProperty("radius") : DefaultRadius;
            var particleSpacing = entity.ContainsKey("particle_spacing") ? entity.GetFloatProperty("particle_spacing") : DefaultParticleSpacing;
            if (baseRadius <= 0f || particleSpacing <= 0f)
            {
                return false;
            }

            var slack = entity.ContainsKey("slack") ? entity.GetFloatProperty("slack") : DefaultSlack;

            var geometry = new CableGeometry
            {
                Nodes = nodes,
                RadiusScales = PathParticleRope.ParseRadiusScales(entity.GetStringProperty("pathnoderadiusscales")),
                Colors = PathParticleRope.ParseColors(entity.GetStringProperty("pathnodecolors")),
                Pins = PathParticleRope.ParsePins(entity.GetStringProperty("pathnodepinsenabled")),
                BaseRadius = baseRadius,
                ParticleSpacing = particleSpacing,
            };

            var samples = CableMeshBuilder.SampleRope(geometry, maxParticles, out var capped);
            if (samples.Count < 2)
            {
                return false;
            }

            if (capped)
            {
                scene.RendererContext.Logger.LogWarning("path_particle_rope '{Target}' exceeded the effect particle budget ({Max}) and was truncated",
                    entity.TargetName, maxParticles);
            }

            // The rope simulates in world space: the entity-local pathnodes are placed by the entity's own
            // transform (origin + angles + scale) and any parent/prefab transform. Gravity stays world-down.
            var worldTransform = EntityTransformHelper.CalculateTransformationMatrix(entity) * parentTransform;
            var snapshot = BuildSnapshot(samples, worldTransform, entity.GetColor32Property("color_tint"));

            // The effect settles via its own m_flPreSimulationTime and freezes via m_flStopSimulationAfterTime
            // when it authors them (the static cable presets do); effects that omit them settle live.
            node = new ParticleSceneNode(scene, particleSystem, snapshot);

            // Control point 1 carries the rope parameters the effect reads: X = radius, Y = slack (the
            // C_OP_RopeSpringConstraint rest length). Set before the first update so the settle uses them.
            node.GetControlPoint(1).Position = new Vector3(baseRadius, slack, 0f);

            return true;
        }

        /// <summary>
        /// Builds a runtime snapshot from the rope samples: world-space positions (via
        /// <paramref name="worldTransform"/>), per-node radius and colour (the node colour multiplied by the
        /// whole-cable <paramref name="colorTint"/>), and a force scale that pins the path nodes (0) and
        /// frees the interior samples (1) so they droop under gravity.
        /// </summary>
        internal static ParticleSnapshot BuildSnapshot(List<RopeSample> samples, Matrix4x4 worldTransform, Vector3 colorTint)
        {
            var count = samples.Count;
            var position = new Vector3[count];
            var radius = new float[count];
            var color = new Vector3[count];
            var forceScale = new float[count];

            for (var i = 0; i < count; i++)
            {
                var s = samples[i];
                position[i] = Vector3.Transform(s.Position, worldTransform);
                radius[i] = s.Radius;
                color[i] = s.Color * colorTint;
                forceScale[i] = s.Pinned ? 0f : 1f;
            }

            var attributes = new Dictionary<(string Name, string Type), IEnumerable>
            {
                [("position", "vector")] = position,
                [("radius", "float")] = radius,
                [("color", "vector")] = color,
                [("force_scale", "float")] = forceScale,
            };

            return ParticleSnapshot.Create((uint)count, attributes);
        }

        private static (ParticleSystem? System, int MaxParticles) LoadEffect(Scene scene, Entity entity)
        {
            var effectName = entity.GetStringProperty("effect_name");
            if (string.IsNullOrEmpty(effectName))
            {
                effectName = DefaultEffectName;
            }

            if (scene.RendererContext.FileLoader.LoadFileCompiled(effectName)?.DataBlock is ParticleSystem loadedSystem)
            {
                return (loadedSystem, loadedSystem.Data.GetInt32Property("m_nMaxParticles", 1000));
            }

            return (null, 0);
        }
    }
}
