using System.Linq;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders each particle as a model from the authored list, placed, turned and scaled by the
    /// particle and tinted by its color. The models are ordinary model scene nodes, so they light,
    /// shadow and depth sort like any other model in the scene; this renderer only keeps one placed
    /// on each live particle.
    /// </summary>
    /// <remarks>
    /// Body groups, sequences and per-particle animation, cloth and material variables are not
    /// applied; every model draws in its bind pose with its default body groups. Models that should
    /// draw only into the effects bloom or water effects pass are not drawn at all.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderModels">C_OP_RenderModels</seealso>
    internal class RenderModels : ParticleFunctionRenderer
    {
        private readonly Scene scene;
        private readonly (Model? Model, float Weight, string[] Skins)[] models;
        private readonly float totalWeight;

        private readonly bool ignoreNormal;
        private readonly bool orientZ;
        private readonly bool ignoreRadius;
        private readonly bool localScale;
        private readonly bool suppressTint;
        private readonly int modelScaleControlPoint = -1;
        private readonly IVectorProvider localOffset = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider localRotation = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider componentScale = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider manualModelSelection = new LiteralNumberProvider(-1f);
        private readonly INumberProvider skin = new LiteralNumberProvider(0f);
        private readonly INumberProvider rollScale = new LiteralNumberProvider(1f);
        private readonly ParticleField alpha2Field = ParticleField.AlphaAlternate;

        // A particle no model was picked for has no node
        private readonly PlacedPerParticle<(ModelSceneNode? Node, int Model, int Skin)> placed = new();
        private readonly Stack<(ModelSceneNode Node, int Skin)>[] idle;

        public RenderModels(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse, scene)
        {
            this.scene = scene;

            var list = new List<(Model?, float, string[])>();

            if (!OnlyRenderInEffectsBloomPass && !OnlyRenderInEffectsWaterPass)
            {
                foreach (var entry in parse.Data.GetArray("m_ModelList") ?? [])
                {
                    var path = entry.GetStringProperty("m_model");
                    var model = string.IsNullOrEmpty(path) ? null : rendererContext.FileLoader.LoadFileCompiled(path)?.DataBlock as Model;

                    // Nodes are only created when particles spawn, by which point the loader may have closed the
                    // model resource, so read the embedded mesh buffers while it is still open
                    model?.GetEmbeddedMeshes();

                    var weight = entry.ContainsKey("m_flRelativeProbabilityOfSpawn") ? entry.GetFloatProperty("m_flRelativeProbabilityOfSpawn") : 1f;
                    list.Add((model, weight, model?.GetMaterialGroups().Select(static group => group.Name).ToArray() ?? []));
                }
            }

            models = [.. list];
            totalWeight = models.Where(static entry => entry.Model != null && entry.Weight > 0f).Sum(static entry => entry.Weight);
            idle = [.. models.Select(static _ => new Stack<(ModelSceneNode, int)>())];

            ignoreNormal = parse.Boolean("m_bIgnoreNormal", ignoreNormal);
            orientZ = parse.Boolean("m_bOrientZ", orientZ);
            ignoreRadius = parse.Boolean("m_bIgnoreRadius", ignoreRadius);
            localScale = parse.Boolean("m_bLocalScale", localScale);
            suppressTint = parse.Boolean("m_bSuppressTint", suppressTint);
            modelScaleControlPoint = parse.Int32("m_nModelScaleCP", modelScaleControlPoint);
            localOffset = parse.VectorProvider("m_vecLocalOffset", localOffset);
            localRotation = parse.VectorProvider("m_vecLocalRotation", localRotation);
            componentScale = parse.VectorProvider("m_vecComponentScale", componentScale);
            manualModelSelection = parse.NumberProvider("m_flManualModelSelection", manualModelSelection);
            skin = parse.NumberProvider("m_nSkin", skin);
            rollScale = parse.NumberProvider("m_flRollScale", rollScale);
            alpha2Field = parse.ParticleField("m_nAlpha2Field", alpha2Field);
        }

        /// <inheritdoc/>
        public override bool DrawsInPasses => false;

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (models.Length == 0)
            {
                return;
            }

            var colorScale = ColorScale.NextVector(systemState);
            var alphaScale = AlphaScale.NextNumber(systemState);
            var radiusScale = RadiusScale.NextNumber(systemState);
            var roll = rollScale.NextNumber(systemState);
            var controlPointScale = modelScaleControlPoint >= 0 ? systemState.GetControlPoint(modelScaleControlPoint).Position : Vector3.One;

            placed.BeginFrame();

            foreach (ref var particle in particles.Current)
            {
                if (!placed.TryGetValue(particle.UniqueParticleId, out var entry))
                {
                    var model = PickModel(ref particle, systemState);
                    entry = model < 0 ? (null, model, 0) : TakeNode(model);
                }

                if (entry.Node is { } node)
                {
                    var skins = models[entry.Model].Skins;
                    var skinIndex = (int)skin.NextNumber(ref particle, systemState);
                    skinIndex = (uint)skinIndex < (uint)skins.Length ? skinIndex : 0;

                    if (skinIndex != entry.Skin)
                    {
                        node.SetMaterialGroup(skins[skinIndex]);
                        entry = entry with { Skin = skinIndex };
                    }

                    var scale = componentScale.NextVector(ref particle, systemState) * controlPointScale
                        * (ignoreRadius ? 1f : particle.Radius * radiusScale);

                    node.Transform = ParticleTransform(ref particle, systemState, scale, roll);
                    node.Tint = suppressTint ? Vector3.One : particle.Color * colorScale;
                    node.Alpha = particle.Alpha * particle.GetScalar(alpha2Field) * alphaScale;
                    node.Visible = true;
                }

                placed.Keep(particle.UniqueParticleId, entry);
            }

            foreach (var entry in placed.Sweep())
            {
                Release(entry);
            }
        }

        /// <inheritdoc/>
        public override void Hide()
        {
            foreach (var entry in placed.Values)
            {
                Release(entry);
            }

            placed.Clear();
        }

        /// <summary>
        /// The model a new particle draws: the manual selection when it names a loaded model, otherwise
        /// one picked at random by relative probability, or -1 when no model can be picked.
        /// </summary>
        private int PickModel(ref Particle particle, ParticleSystemState systemState)
        {
            var manual = (int)manualModelSelection.NextNumber(ref particle, systemState);

            if ((uint)manual < (uint)models.Length && models[manual].Model != null)
            {
                return manual;
            }

            if (totalWeight <= 0f)
            {
                return -1;
            }

            var pick = systemState.Random.ForParticle(particle.ParticleId) * totalWeight;
            var picked = -1;

            for (var i = 0; i < models.Length; i++)
            {
                if (models[i].Model == null || models[i].Weight <= 0f)
                {
                    continue;
                }

                picked = i;
                pick -= models[i].Weight;

                if (pick <= 0f)
                {
                    break;
                }
            }

            return picked;
        }

        private (ModelSceneNode Node, int Model, int Skin) TakeNode(int model)
        {
            if (idle[model].TryPop(out var pooled))
            {
                return (pooled.Node, model, pooled.Skin);
            }

            var node = new ModelSceneNode(scene, models[model].Model!, isWorldPreview: true);
            scene.Add(node, true);

            return (node, model, 0);
        }

        /// <summary>Hides a particle's node and hands it back for the next particle of that model.</summary>
        private void Release((ModelSceneNode? Node, int Model, int Skin) entry)
        {
            if (entry.Node is not { } node)
            {
                return;
            }

            node.Visible = false;
            idle[entry.Model].Push((node, entry.Skin));
        }

        /// <summary>
        /// Model space to world. The model takes the local offset, then the scale, the authored local
        /// rotation and the particle's own angles, and is then turned into the frame its normal gives,
        /// unless the normal is ignored. A local scale applies along the model's own axes, otherwise it
        /// applies after the turns, along the axes of that frame.
        /// </summary>
        private Matrix4x4 ParticleTransform(ref Particle particle, ParticleSystemState systemState, Vector3 scale, float roll)
        {
            var angles = particle.Rotation;
            var turn = EntityTransformHelper.EulerAnglesToRotationMatrix(localRotation.NextVector(ref particle, systemState))
                * EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(angles.Y, angles.X, angles.Z * roll));

            var scaling = Matrix4x4.CreateScale(Vector3.Max(scale, new Vector3(ParticleMath.FloatEpsilon)));
            var basis = ignoreNormal ? Matrix4x4.Identity : NormalBasis(particle.Normal, orientZ);
            var model = localScale ? scaling * turn * basis : turn * scaling * basis;

            return Matrix4x4.CreateTranslation(localOffset.NextVector(ref particle, systemState)) * model
                * Matrix4x4.CreateTranslation(particle.Position);
        }

        /// <summary>
        /// The frame a normal turns the model into: the normal becomes the model's forward axis, or its
        /// up axis when oriented on Z, and the other two axes come from a fixed perpendicular to it.
        /// </summary>
        private static Matrix4x4 NormalBasis(Vector3 normal, bool orientZ)
        {
            var length = normal.Length();

            if (length == 0f || !float.IsFinite(length))
            {
                return Matrix4x4.Identity;
            }

            var direction = normal / length;
            var perpendicular = MathUtils.SafeNormalize(new Vector3(((1f - direction.Z) * direction.Y * direction.Y) + direction.Z, 0f, -direction.X));
            perpendicular = MathUtils.SafeNormalize(perpendicular - (direction * Vector3.Dot(perpendicular, direction)));
            var third = Vector3.Cross(direction, perpendicular);

            var (x, y, z) = orientZ ? (third, -perpendicular, direction) : (direction, perpendicular, third);

            return new Matrix4x4(
                x.X, x.Y, x.Z, 0f,
                y.X, y.Y, y.Z, 0f,
                z.X, z.Y, z.Z, 0f,
                0f, 0f, 0f, 1f);
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            // The model nodes draw themselves with the rest of the scene
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            foreach (var (node, _, _) in placed.Values)
            {
                if (node != null)
                {
                    RemoveNode(node);
                }
            }

            foreach (var stack in idle)
            {
                foreach (var (node, _) in stack)
                {
                    RemoveNode(node);
                }

                stack.Clear();
            }

            placed.Clear();
        }

        /// <summary>Takes a node out of the scene and deletes it, unless the scene no longer holds it, as when it deletes all of its nodes itself.</summary>
        private void RemoveNode(ModelSceneNode node)
        {
            if (scene.Remove(node, true))
            {
                node.Delete();
            }
        }
    }
}
