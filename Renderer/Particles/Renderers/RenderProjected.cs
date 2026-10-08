using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.Decals;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders each particle as a decal projected onto the scene around it, through the scene's
    /// projected decal system: a box as wide as the particle, projecting along its normal or straight
    /// down, which follows the particle and is removed when it dies.
    /// </summary>
    /// <remarks>
    /// Material variables and sheet animation are not applied, and the decals project onto whatever the
    /// scene depth holds whatever the world, water and character switches say.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderProjected">C_OP_RenderProjected</seealso>
    internal class RenderProjected : ParticleFunctionRenderer
    {
        private readonly ProjectedDecalSystem decals;
        private readonly int[] materials;
        private readonly bool flipHorizontal;
        private readonly bool orientToNormal;
        private readonly bool depthControls;
        private readonly float minProjectionDepth = -128f;
        private readonly float maxProjectionDepth = 128f;
        private readonly INumberProvider materialSelection = new LiteralNumberProvider(0f);
        private readonly INumberProvider rollScale = new LiteralNumberProvider(1f);
        private readonly ParticleField alpha2Field = ParticleField.AlphaAlternate;

        private readonly Dictionary<int, ProjectedDecalHandle> placed = [];
        private readonly HashSet<int> seen = [];
        private readonly List<int> gone = [];

        public RenderProjected(ParticleDefinitionParser parse, Scene scene) : base(parse, scene)
        {
            decals = scene.ProjectedDecals;

            var registered = new List<int>();

            foreach (var entry in parse.Data.GetArray("m_vecProjectedMaterials") ?? [])
            {
                var path = entry.GetStringProperty("m_hMaterial");

                if (!string.IsNullOrEmpty(path) && decals.RegisterMaterial(path) is var handle and >= 0)
                {
                    registered.Add(handle);
                }
            }

            materials = [.. registered];

            flipHorizontal = parse.Boolean("m_bFlipHorizontal", flipHorizontal);
            orientToNormal = parse.Boolean("m_bOrientToNormal", orientToNormal);
            depthControls = parse.Boolean("m_bEnableProjectedDepthControls", depthControls);
            minProjectionDepth = parse.Float("m_flMinProjectionDepth", minProjectionDepth);
            maxProjectionDepth = parse.Float("m_flMaxProjectionDepth", maxProjectionDepth);
            materialSelection = parse.NumberProvider("m_flMaterialSelection", materialSelection);
            rollScale = parse.NumberProvider("m_flRollScale", rollScale);
            alpha2Field = parse.ParticleField("m_nAlpha2Field", alpha2Field);
        }

        /// <summary>Whether any of the materials loaded as a projected decal, so a system that draws nothing can be reported.</summary>
        public bool HasMaterials => materials.Length > 0;

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (materials.Length == 0)
            {
                return;
            }

            var shown = OwnerNode is not { Visible: false } && OwnerNode?.LayerEnabled != false;
            var radiusScale = RadiusScale.NextNumber(systemState);
            var alphaScale = AlphaScale.NextNumber(systemState);
            var colorScale = ColorScale.NextVector(systemState);
            var roll = rollScale.NextNumber(systemState);

            seen.Clear();

            foreach (ref var particle in particles.Current)
            {
                if (!shown)
                {
                    break;
                }

                var box = DecalBox(ref particle, radiusScale, roll);
                var tint = new Vector4(particle.Color * colorScale, particle.Alpha * particle.GetScalar(alpha2Field) * alphaScale);

                if (placed.TryGetValue(particle.UniqueParticleId, out var handle))
                {
                    decals.Move(handle, box, tint);
                }
                else
                {
                    var selection = Math.Clamp((int)materialSelection.NextNumber(ref particle, systemState), 0, materials.Length - 1);
                    handle = decals.Add(materials[selection], box, tint, flipHorizontal, permanent: true);
                    placed.Add(particle.UniqueParticleId, handle);
                }

                seen.Add(particle.UniqueParticleId);
            }

            gone.Clear();

            foreach (var (id, handle) in placed)
            {
                if (!seen.Contains(id))
                {
                    decals.Remove(handle);
                    gone.Add(id);
                }
            }

            foreach (var id in gone)
            {
                placed.Remove(id);
            }
        }

        /// <summary>
        /// The box the particle's decal projects through: as wide as the particle, turned by its roll,
        /// projecting along its normal when authored to and straight down otherwise. The depth range,
        /// when enabled, places the box along the projection axis instead of centring it on the particle.
        /// </summary>
        private Matrix4x4 DecalBox(ref Particle particle, float radiusScale, float roll)
        {
            var normal = orientToNormal ? particle.GetVector(ParticleField.Normal) : Vector3.UnitZ;
            normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitZ;

            var width = particle.Radius * radiusScale * 2f;
            var center = particle.Position;
            var depth = width;

            if (depthControls)
            {
                center += normal * ((minProjectionDepth + maxProjectionDepth) * 0.5f);
                depth = MathF.Max(maxProjectionDepth - minProjectionDepth, 1f);
            }

            // Spin the texture's top edge about the projection axis by the particle's roll
            var reference = MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
            var up = Vector3.Transform(Vector3.Normalize(Vector3.Cross(normal, reference)), Quaternion.CreateFromAxisAngle(normal, particle.Rotation.Z * roll));

            return ProjectedDecalSystem.CreateBoxTransform(center, normal, up, new Vector3(width, width, depth));
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            // The decals draw in the scene's decal pass
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            foreach (var handle in placed.Values)
            {
                decals.Remove(handle);
            }

            placed.Clear();
        }
    }
}
