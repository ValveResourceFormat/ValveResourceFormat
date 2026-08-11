using System.Collections;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Builds the particle system a <c>.vsnap</c> is previewed through: an ordinary definition that binds the
    /// snapshot to <see cref="SnapshotControlPoint"/> and reads every attribute it stores back out of it
    /// through <c>C_INIT_InitFromCPSnapshot</c>, one particle per snapshot element.
    /// </summary>
    public static class SnapshotParticleSystem
    {
        /// <summary>The control point the snapshot is bound to, and that the initializers read back from.</summary>
        public const int SnapshotControlPoint = 0;

        /// <summary>
        /// The control point whose X component carries the on-screen quad size, written by
        /// <see cref="SetScreenSize"/>. Only used when <see cref="UsesConstantScreenSize"/>.
        /// </summary>
        public const int ScreenSizeControlPoint = 1;

        /// <summary>Default quad height, as a fraction of the viewport height.</summary>
        public const float DefaultScreenSize = 0.004f;

        /// <summary>
        /// The most particles one system can hold. A larger snapshot is truncated to its first
        /// <see cref="MaxParticles"/> elements.
        /// </summary>
        public static int MaxParticles => ParticleCollection.MAX_PARTICLES;

        // Every field the snapshot format has a name for, in the order they are written into the particle.
        // Declaration order would do except for two that write over something another one needs: Normal
        // shares its storage with Yaw and Pitch, and ParticleId seeds the per-particle random draws.
        private static readonly ParticleField[] ReadFields =
        [
            ParticleField.Position,
            ParticleField.PositionPrevious,
            ParticleField.Radius,
            ParticleField.Normal,
            ParticleField.Roll,
            ParticleField.Yaw,
            ParticleField.Pitch,
            ParticleField.RollSpeed,
            ParticleField.Color,
            ParticleField.Alpha,
            ParticleField.AlphaAlternate,
            ParticleField.GlowRgb,
            ParticleField.GlowAlpha,
            ParticleField.LifeDuration,
            ParticleField.CreationTime,
            ParticleField.SequenceNumber,
            ParticleField.SecondSequenceNumber,
            ParticleField.ManualAnimationFrame,
            ParticleField.TrailLength,
            ParticleField.ForceScale,
            ParticleField.HitboxIndex,
            ParticleField.HitboxOffsetPosition,
            ParticleField.ScratchVector,
            ParticleField.ScratchFloat,
            ParticleField.ShaderExtraData1,
            ParticleField.ShaderExtraData2,
            ParticleField.ParticleId,
        ];

        /// <summary>
        /// Whether the snapshot holds anything this preview can place, i.e. a position attribute of the
        /// expected type and at least one particle.
        /// </summary>
        public static bool CanPreview(ParticleSnapshot snapshot)
            => snapshot.NumParticles > 0 && HasReadableAttribute(snapshot, ParticleField.Position);

        /// <summary>
        /// Whether the preview has to invent a size because the snapshot stores no radius, in which case
        /// the quads are drawn at a constant size on screen and <see cref="SetScreenSize"/> controls it.
        /// </summary>
        public static bool UsesConstantScreenSize(ParticleSnapshot snapshot)
            => !HasReadableAttribute(snapshot, ParticleField.Radius);

        /// <summary>
        /// Builds the preview effect. The snapshot must still be handed to the
        /// <see cref="ParticleSceneNode"/> that runs the returned system, which is what binds it.
        /// </summary>
        public static ParticleSystem Create(ParticleSnapshot snapshot)
        {
            var count = Math.Min((int)Math.Min(snapshot.NumParticles, int.MaxValue), MaxParticles);
            var constantScreenSize = UsesConstantScreenSize(snapshot);

            var initializers = KVObject.Array();

            foreach (var field in ReadFields)
            {
                if (HasReadableAttribute(snapshot, field))
                {
                    initializers.Add(MakeSnapshotInitializer(field));
                }
            }

            var system = KVObject.Collection();
            system.Add("_class", "CParticleSystemDefinition");
            system.Add("m_nBehaviorVersion", 13);
            system.Add("m_nMaxParticles", count);
            system.Add("m_nSnapshotControlPoint", SnapshotControlPoint);

            if (constantScreenSize)
            {
                // Only has to be non-zero: the renderer divides it back out resolving the screen size.
                system.Add("m_flConstantRadius", 1f);
            }

            system.Add("m_Emitters", SingleItemArray(MakeEmitter(count)));
            system.Add("m_Initializers", initializers);
            system.Add("m_Renderers", SingleItemArray(MakeRenderer(constantScreenSize)));

            return ParticleSystem.Create(system);
        }

        /// <summary>
        /// Sets the on-screen size of the quads drawn by the effect <paramref name="node"/> runs.
        /// </summary>
        /// <param name="node">The node running a system built by <see cref="Create"/>.</param>
        /// <param name="screenFraction">Quad height as a fraction of the viewport height.</param>
        /// <param name="verticalFieldOfView">The camera's vertical field of view, in radians.</param>
        public static void SetScreenSize(ParticleSceneNode node, float screenFraction, float verticalFieldOfView)
        {
            // The renderer reads the control point as a half-width per unit of camera distance.
            var slope = screenFraction * MathF.Tan(verticalFieldOfView * 0.5f);
            node.GetControlPoint(ScreenSizeControlPoint).Position = new Vector3(slope, 0f, 0f);
        }

        /// <summary>
        /// The world-space box containing the snapshot's particles, for framing the camera on it.
        /// </summary>
        public static AABB GetBounds(ParticleSnapshot snapshot)
        {
            if (GetReadableAttribute(snapshot, ParticleField.Position) is not Vector3[] positions || positions.Length == 0)
            {
                return new AABB(Vector3.Zero, Vector3.Zero);
            }

            var radii = GetReadableAttribute(snapshot, ParticleField.Radius) as float[];

            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);

            for (var i = 0; i < positions.Length; i++)
            {
                var radius = new Vector3(radii != null && i < radii.Length ? radii[i] : 0f);

                min = Vector3.Min(min, positions[i] - radius);
                max = Vector3.Max(max, positions[i] + radius);
            }

            return new AABB(min, max);
        }

        // Pointed at the snapshot control point too, which is what makes it emit one particle per element.
        private static KVObject MakeEmitter(int count)
        {
            var emitter = KVObject.Collection();
            emitter.Add("_class", "C_OP_InstantaneousEmitter");
            emitter.Add("m_nParticlesToEmit", count);
            emitter.Add("m_nSnapshotControlPoint", SnapshotControlPoint);
            return emitter;
        }

        private static KVObject MakeSnapshotInitializer(ParticleField field)
        {
            var initializer = KVObject.Collection();
            initializer.Add("_class", "C_INIT_InitFromCPSnapshot");
            initializer.Add("m_nControlPointNumber", SnapshotControlPoint);
            initializer.Add("m_nAttributeToWrite", (int)field);
            initializer.Add("m_nAttributeToRead", (int)field);
            // Snapshot values are world space already, rather than the default of control point 0's frame.
            initializer.Add("m_nLocalSpaceCP", -1);
            return initializer;
        }

        // Without a radius in the snapshot there is no world size to draw at, so one is invented:
        // m_bDistanceAlpha turns the min/max size pair from a clamp in world units into one measured per
        // unit of camera distance, and pinning both ends of that clamp to the same value makes the quad a
        // constant size on screen whatever its radius or distance.
        private static KVObject MakeRenderer(bool constantScreenSize)
        {
            var renderer = KVObject.Collection();
            renderer.Add("_class", "C_OP_RenderSprites");

            if (constantScreenSize)
            {
                renderer.Add("m_bDistanceAlpha", true);
                renderer.Add("m_flMinSize", MakeScreenSizeProvider());
                renderer.Add("m_flMaxSize", MakeScreenSizeProvider());
            }

            return renderer;
        }

        private static KVObject MakeScreenSizeProvider()
        {
            var provider = KVObject.Collection();
            provider.Add("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
            provider.Add("m_nControlPoint", ScreenSizeControlPoint);
            provider.Add("m_nVectorComponent", 0);
            return provider;
        }

        private static KVObject SingleItemArray(KVObject item)
        {
            var array = KVObject.Array();
            array.Add(item);
            return array;
        }

        private static bool HasReadableAttribute(ParticleSnapshot snapshot, ParticleField field)
            => GetReadableAttribute(snapshot, field) != null;

        // Snapshot columns are typed by the file, not by the field they map onto, and the sampler silently
        // ignores a mismatch. Pairing them here keeps an initializer that could never write anything out.
        private static IEnumerable? GetReadableAttribute(ParticleSnapshot snapshot, ParticleField field)
        {
            var name = ParticleSnapshot.GetSnapshotAttributeName(field);

            if (name == null)
            {
                return null;
            }

            foreach (var ((attributeName, _), data) in snapshot.AttributeData)
            {
                if (attributeName != name)
                {
                    continue;
                }

                return field.FieldType() switch
                {
                    "vector" => data as Vector3[],
                    "float" => data as float[],
                    _ => null,
                };
            }

            return null;
        }
    }
}
