using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.Decals;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders each particle as a decal projected onto the scene around it, through the scene's
    /// projected decal system: a box as wide as the particle, projecting along its normal or straight
    /// down, which follows the particle and is removed when it dies or shrinks to nothing. The decals
    /// share the decal system's room with every other decal and make way for newer ones like them.
    /// </summary>
    /// <remarks>
    /// Material variables, sheet animation and the world, water and character projection switches are not
    /// applied: the decals project onto whatever the scene depth holds.
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

        private readonly PlacedPerParticle<(ProjectedDecalHandle Handle, int Material)> placed = new();

        public RenderProjected(ParticleDefinitionParser parse, Scene scene) : base(parse, scene)
        {
            decals = scene.ProjectedDecals;

            var registered = new List<int>();

            foreach (var entry in parse.Data.GetArray("m_vecProjectedMaterials") ?? [])
            {
                var path = entry.GetStringProperty("m_hMaterial");

                registered.Add(string.IsNullOrEmpty(path) ? -1 : decals.RegisterMaterial(path));
            }

            materials = [.. registered];

            flipHorizontal = parse.Boolean("m_bFlipHorizontal", flipHorizontal);
            orientToNormal = parse.Boolean("m_bOrientToNormal", orientToNormal);
            depthControls = parse.Boolean("m_bEnableProjectedDepthControls", depthControls);
            minProjectionDepth = parse.Float("m_flMinProjectionDepth", minProjectionDepth);
            maxProjectionDepth = parse.Float("m_flMaxProjectionDepth", maxProjectionDepth);
            materialSelection = parse.NumberProvider("m_flMaterialSelection", materialSelection);
        }

        /// <inheritdoc/>
        public override bool DrawsInPasses => false;

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (materials.Length == 0)
            {
                return;
            }

            var scales = EvaluateScales(systemState);

            placed.BeginFrame();

            foreach (var particle in particles.Current)
            {
                var width = particle.Radius * scales.Radius * 2f;

                if (width <= 0f)
                {
                    continue;
                }

                var material = materials[Math.Clamp((int)materialSelection.NextNumber(particle, systemState), 0, materials.Length - 1)];

                if (material < 0)
                {
                    placed.Keep(particle.UniqueParticleId);
                    continue;
                }

                var box = DecalBox(particle, width, scales.Roll);
                var tint = BlendColor(particle, scales);

                if (placed.TryGetValue(particle.UniqueParticleId, out var entry))
                {
                    if (entry.Material == material && decals.Move(entry.Handle, box, tint))
                    {
                        placed.Keep(particle.UniqueParticleId);
                        continue;
                    }

                    decals.Remove(entry.Handle);
                }

                var handle = decals.AddFollowing(material, box, tint, flipHorizontal);

                if (handle.IsValid)
                {
                    placed.Keep(particle.UniqueParticleId, (handle, material));
                }
            }

            foreach (var (handle, _) in placed.Sweep())
            {
                decals.Remove(handle);
            }
        }

        /// <inheritdoc/>
        public override void Hide() => Delete();

        /// <summary>
        /// The box the particle's decal projects through: as wide as the particle, projecting along its
        /// normal when authored to and straight down otherwise. The texture's top runs along the normal
        /// crossed with world Y, or world X for a normal close to Y, or along -X when projecting down,
        /// turned back about the projection axis by the particle's roll. The depth range, when enabled,
        /// places the box along the projection axis instead of centring it on the particle.
        /// </summary>
        private Matrix4x4 DecalBox(Particle particle, float width, float roll)
        {
            var normal = orientToNormal ? particle.Normal : Vector3.UnitZ;
            var top = orientToNormal
                ? Vector3.Cross(normal.Y > 0.99f ? Vector3.UnitX : Vector3.UnitY, normal)
                : -Vector3.UnitX;

            normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitZ;

            var center = particle.Position;
            var depth = width;

            if (depthControls)
            {
                center += normal * ((minProjectionDepth + maxProjectionDepth) * 0.5f);
                depth = MathF.Max(maxProjectionDepth - minProjectionDepth, 1f);
            }

            var up = Vector3.Transform(top, Quaternion.CreateFromAxisAngle(normal, -(roll * particle.Rotation.Z)));

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
            foreach (var (handle, _) in placed.Values)
            {
                decals.Remove(handle);
            }

            placed.Clear();
        }
    }
}
