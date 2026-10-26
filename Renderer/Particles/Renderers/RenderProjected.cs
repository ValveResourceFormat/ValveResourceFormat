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
    /// Material variables and sheet animation are not applied. The decals project onto whatever the scene
    /// depth holds, and nothing at all when they do not project onto the world.
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

        /// <summary>
        /// Whether the decals project onto the world. The scene's decal pass cannot tell the world from the
        /// other surfaces it projects onto, so without it nothing is projected.
        /// </summary>
        private readonly bool projectWorld = true;

        /// <summary>The decal of each particle with a handle, at the slot its handle names less one.</summary>
        private readonly ParticleHandlePool<(ProjectedDecalHandle Handle, int Material)> pool = new();
        private readonly Action<(ProjectedDecalHandle Handle, int Material)> removeDecal;

        public RenderProjected(ParticleDefinitionParser parse, Scene scene) : base(parse, scene)
        {
            decals = scene.ProjectedDecals;
            removeDecal = entry => decals.Remove(entry.Handle);

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
            projectWorld = parse.Boolean("m_bProjectWorld", projectWorld);

            // Projecting onto characters (m_bProjectCharacter) and water (m_bProjectWater) is left to the host.
            // The decals have no sheet animation for m_flAnimationTimeScale to speed up, and m_MaterialVars
            // name dynamic material attributes, which the decal materials do not take per decal.
        }

        /// <inheritdoc/>
        public override bool DrawsInPasses => false;

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (materials.Length == 0 || !projectWorld)
            {
                return;
            }

            var column = pool.Claim(particles);
            pool.ReleaseDead(particles, column, removeDecal);

            var scales = EvaluateScales(systemState);
            var current = particles.Current;
            var handles = particles.Handles(column);
            var positions = current.ReadVectors(ParticleField.Position);
            var normals = current.ReadVectors(ParticleField.Normal);
            var colors = current.ReadVectors(ParticleField.Color);
            var radii = current.ReadFloats(ParticleField.Radius);
            var rolls = current.ReadFloats(ParticleField.Roll);
            var alphas = current.ReadFloats(ParticleField.Alpha);
            var alpha2s = ReadAlpha2(current);
            var materialSelections = materialSelection.Fill(current, systemState);

            for (var i = 0; i < current.Length; i++)
            {
                var slot = handles[i] - 1;
                var width = radii[i] * scales.Radius * 2f;

                if (width <= 0f)
                {
                    if (slot >= 0)
                    {
                        FreeSlot(slot);
                        handles[i] = 0;
                    }

                    continue;
                }

                var material = materials[Math.Clamp((int)materialSelections[i], 0, materials.Length - 1)];

                if (material < 0)
                {
                    continue;
                }

                var box = DecalBox(positions[i], normals[i], width, scales.Roll * rolls[i]);
                // The colour and alpha are truncated to bytes without a clamp, so a value past 1 wraps around
                var alphaByte = (byte)(int)(alphas[i] * alpha2s[i] * 255f);
                var tint = ParticleColorBlend.Apply(ColorBlendType, ParticleColorBlend.ToWrappedBytes(colors[i], alphaByte), scales.Color) / 255f;

                if (slot >= 0)
                {
                    var entry = pool[slot];

                    if (entry.Material == material && decals.Move(entry.Handle, box, tint))
                    {
                        continue;
                    }

                    decals.Remove(entry.Handle);
                }

                var handle = decals.AddFollowing(material, box, tint, flipHorizontal);

                if (!handle.IsValid)
                {
                    if (slot >= 0)
                    {
                        FreeSlot(slot);
                        handles[i] = 0;
                    }

                    continue;
                }

                if (slot < 0)
                {
                    handles[i] = pool.Allocate((handle, material)) + 1;
                }
                else
                {
                    pool[slot] = (handle, material);
                }
            }
        }

        /// <summary>Removes the decal at a slot and makes the slot available again.</summary>
        private void FreeSlot(int slot)
        {
            decals.Remove(pool[slot].Handle);
            pool.Free(slot);
        }

        /// <inheritdoc/>
        public override void Hide(ParticleCollection particles)
        {
            if (pool.Column is { } column)
            {
                pool.ReleaseDead(particles, column, removeDecal);
            }

            RemoveDecals();
        }

        /// <summary>Removes every decal, keeping the slots with their particles.</summary>
        private void RemoveDecals()
        {
            for (var i = 0; i < pool.Count; i++)
            {
                decals.Remove(pool[i].Handle);
                pool[i] = default;
            }
        }

        /// <summary>
        /// The box the particle's decal projects through: as wide as the particle, projecting along its
        /// normal when authored to and straight down otherwise. The texture's top runs along the normal
        /// crossed with world Y, or world X for a normal close to Y, or along -X when projecting down,
        /// turned back about the projection axis by the particle's roll. The depth range, when enabled,
        /// places the box along the projection axis instead of centring it on the particle.
        /// </summary>
        private Matrix4x4 DecalBox(Vector3 position, Vector3 particleNormal, float width, float roll)
        {
            var normal = orientToNormal ? particleNormal : Vector3.UnitZ;
            var top = orientToNormal
                ? Vector3.Cross(normal.Y > 0.99f ? Vector3.UnitX : Vector3.UnitY, normal)
                : -Vector3.UnitX;

            normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitZ;

            var center = position;
            var depth = width;

            if (depthControls)
            {
                center += normal * ((minProjectionDepth + maxProjectionDepth) * 0.5f);
                depth = MathF.Max(maxProjectionDepth - minProjectionDepth, 1f);
            }

            var up = Vector3.Transform(top, Quaternion.CreateFromAxisAngle(normal, -roll));

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
            RemoveDecals();
            pool.Clear();
        }
    }
}
