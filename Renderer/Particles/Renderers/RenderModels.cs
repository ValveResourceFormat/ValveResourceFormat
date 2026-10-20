using System.Linq;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders each particle as the model its model input's control point carries or a model from the
    /// authored list, placed, turned and scaled by the particle and tinted by its color. The models are
    /// ordinary model scene nodes, so they light, shadow and depth sort like any other model in the
    /// scene; this renderer only keeps one placed on each live particle. An override material replaces
    /// the materials of every mesh.
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
        private readonly List<(Model? Model, float Weight, string[] Skins)> models = [];
        private readonly float totalWeight;
        private readonly int modelInputControlPoint = -1;
        private readonly Dictionary<Model, int> inputModels = [];

        private readonly bool ignoreNormal;
        private readonly bool orientZ;
        private readonly bool ignoreRadius;
        private readonly bool localScale;
        private readonly bool suppressTint;
        private readonly bool centerOffset;
        private readonly int modelScaleControlPoint = -1;
        private readonly IVectorProvider localOffset = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider localRotation = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider componentScale = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider manualModelSelection = new LiteralNumberProvider(-1f);
        private readonly INumberProvider skin = new LiteralNumberProvider(0f);
        private readonly string? overrideMaterial;
        private readonly bool overrideTranslucentMaterials = true;

        // A particle no model was picked for has no node
        private readonly PlacedPerParticle<(ModelSceneNode? Node, int Model, int Skin)> placed = new();

        /// <summary>The model each live particle was given when it was created, by unique particle id, or -1 for none.</summary>
        private readonly Dictionary<int, int> pickedModels = [];
        private readonly HashSet<int> liveParticleIds = [];
        private readonly List<Stack<(ModelSceneNode Node, int Skin)>> idle;

        public RenderModels(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse, scene)
        {
            this.scene = scene;

            if (!OnlyRenderInEffectsBloomPass && !OnlyRenderInEffectsWaterPass)
            {
                // TODO: Named value model and entity handle inputs
                if (parse.Data.GetSubCollection("m_modelInput") is { IsCollection: true } modelInputData)
                {
                    var modelInput = parse.Nested(modelInputData);

                    if (modelInput.Enum("m_nType", ParticleModelType.PM_TYPE_INVALID) == ParticleModelType.PM_TYPE_CONTROL_POINT)
                    {
                        modelInputControlPoint = modelInput.Int32("m_nControlPoint", modelInputControlPoint);
                    }
                }

                foreach (var entry in parse.Data.GetArray("m_ModelList") ?? [])
                {
                    var path = entry.GetStringProperty("m_model");
                    var model = string.IsNullOrEmpty(path) ? null : rendererContext.FileLoader.LoadFileCompiled(path)?.DataBlock as Model;

                    // Nodes are only created when particles spawn, by which point the loader may have closed the
                    // model resource, so read the embedded mesh buffers while it is still open
                    model?.GetEmbeddedMeshes();

                    var weight = entry.ContainsKey("m_flRelativeProbabilityOfSpawn") ? entry.GetFloatProperty("m_flRelativeProbabilityOfSpawn") : 1f;
                    models.Add((model, weight, MaterialGroupNames(model)));
                }
            }

            totalWeight = models.Where(static entry => entry.Model != null && entry.Weight > 0f).Sum(static entry => entry.Weight);
            idle = [.. models.Select(static _ => new Stack<(ModelSceneNode, int)>())];

            ignoreNormal = parse.Boolean("m_bIgnoreNormal", ignoreNormal);
            orientZ = parse.Boolean("m_bOrientZ", orientZ);
            ignoreRadius = parse.Boolean("m_bIgnoreRadius", ignoreRadius);
            localScale = parse.Boolean("m_bLocalScale", localScale);
            suppressTint = parse.Boolean("m_bSuppressTint", suppressTint);
            centerOffset = parse.Boolean("m_bCenterOffset", centerOffset);
            modelScaleControlPoint = parse.Int32("m_nModelScaleCP", modelScaleControlPoint);
            localOffset = parse.VectorProvider("m_vecLocalOffset", localOffset);
            localRotation = parse.VectorProvider("m_vecLocalRotation", localRotation);
            componentScale = parse.VectorProvider("m_vecComponentScale", componentScale);
            manualModelSelection = parse.NumberProvider("m_flManualModelSelection", manualModelSelection);
            skin = parse.NumberProvider("m_nSkin", skin);
            overrideTranslucentMaterials = parse.Boolean("m_bOverrideTranslucentMaterials", overrideTranslucentMaterials);
            overrideMaterial = parse.Data.GetStringProperty("m_hOverrideMaterial") is { Length: > 0 } name ? name : null;
        }

        /// <inheritdoc/>
        public override bool DrawsInPasses => false;

        /// <inheritdoc/>
        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(modelScaleControlPoint);
        }

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (models.Count == 0)
            {
                return;
            }

            var scales = EvaluateScales(systemState);
            var controlPointScale = modelScaleControlPoint >= 0 ? systemState.GetControlPoint(modelScaleControlPoint).Position : Vector3.One;

            placed.BeginFrame();

            foreach (ref var particle in particles.Current)
            {
                var model = pickedModels.GetValueOrDefault(particle.UniqueParticleId, -1);
                var skinIndex = (int)skin.NextNumber(ref particle, systemState);

                if (!placed.TryGetValue(particle.UniqueParticleId, out var entry) || entry.Model != model)
                {
                    Release(entry);
                    entry = model < 0 ? (null, model, 0) : TakeNode(model);
                }

                if (entry.Node is { } node)
                {
                    var skins = models[entry.Model].Skins;
                    var modelSkin = (uint)skinIndex < (uint)skins.Length ? skinIndex : 0;

                    if (modelSkin != entry.Skin)
                    {
                        node.SetMaterialGroup(skins[modelSkin]);
                        entry = entry with { Skin = modelSkin };
                    }

                    var scale = componentScale.NextVector(ref particle, systemState) * controlPointScale
                        * (ignoreRadius ? 1f : MathF.Max(ParticleMath.FloatEpsilon, particle.Radius * scales.Radius));

                    // The alpha scale applies twice: once with the particle's alpha and again in the blend
                    var color = BlendColor(ref particle, scales, scales.Alpha);

                    node.Transform = ParticleTransform(ref particle, systemState, node, scale, scales.Roll);
                    node.Tint = suppressTint ? Vector3.One : color.AsVector3();
                    node.Alpha = color.W;
                    node.Visible = true;
                }

                placed.Keep(particle.UniqueParticleId, entry);
            }

            foreach (var entry in placed.Sweep())
            {
                Release(entry);
            }

            if (pickedModels.Count > particles.Count)
            {
                ForgetDeadParticles(particles);
            }
        }

        /// <inheritdoc/>
        public override void InitializeParticles(ParticleCollection particles, ParticleSystemState systemState, int first, int count)
        {
            if (models.Count == 0 && modelInputControlPoint < 0)
            {
                return;
            }

            foreach (ref var particle in particles.Current.Slice(first, count))
            {
                pickedModels[particle.UniqueParticleId] = PickModel(ref particle, systemState);
            }
        }

        private void ForgetDeadParticles(ParticleCollection particles)
        {
            liveParticleIds.Clear();

            foreach (ref var particle in particles.Current)
            {
                liveParticleIds.Add(particle.UniqueParticleId);
            }

            foreach (var particleId in pickedModels.Keys)
            {
                if (!liveParticleIds.Contains(particleId))
                {
                    pickedModels.Remove(particleId);
                }
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
        /// The model a new particle draws: the model bound to the model input's control point when there
        /// is one, otherwise the list entry the manual selection names when it is in range, otherwise one
        /// picked by relative probability with one draw from the system's random sequence, or -1 when
        /// there is no model to draw.
        /// </summary>
        private int PickModel(ref Particle particle, ParticleSystemState systemState)
        {
            if (systemState.GetControlPointModel(modelInputControlPoint) is { } inputModel)
            {
                return InputModelIndex(inputModel);
            }

            var manual = (int)manualModelSelection.NextNumber(ref particle, systemState);

            if ((uint)manual < (uint)models.Count)
            {
                return models[manual].Model != null ? manual : -1;
            }

            if (totalWeight <= 0f)
            {
                return -1;
            }

            var pick = systemState.Random.Next() * totalWeight;
            var picked = -1;

            for (var i = 0; i < models.Count; i++)
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

        /// <summary>Where a model a control point supplies sits in the model list, adding it with no spawn weight the first time.</summary>
        private int InputModelIndex(Model model)
        {
            if (!inputModels.TryGetValue(model, out var index))
            {
                index = models.Count;
                inputModels.Add(model, index);
                models.Add((model, 0f, MaterialGroupNames(model)));
                idle.Add(new Stack<(ModelSceneNode, int)>());
            }

            return index;
        }

        private static string[] MaterialGroupNames(Model? model)
            => model?.GetMaterialGroups().Select(static group => group.Name).ToArray() ?? [];

        private (ModelSceneNode Node, int Model, int Skin) TakeNode(int model)
        {
            if (idle[model].TryPop(out var pooled))
            {
                return (pooled.Node, model, pooled.Skin);
            }

            var node = new ModelSceneNode(scene, models[model].Model!, isWorldPreview: true);
            ApplyOverrideMaterial(node);
            scene.Add(node, true);

            return (node, model, 0);
        }

        /// <summary>
        /// Draws every mesh of a node with the override material. When translucent materials are not
        /// overridden, a draw whose material is translucent or ignores material overrides keeps its own
        /// material, unless that material forces overrides.
        /// </summary>
        private void ApplyOverrideMaterial(ModelSceneNode node)
        {
            if (overrideMaterial == null)
            {
                return;
            }

            foreach (var mesh in node.AllRenderableMeshes)
            {
                mesh.ReplaceMaterials(overrideMaterial, ShouldOverride);
            }
        }

        private bool ShouldOverride(RenderMaterial material)
        {
            var attributes = material.Material.IntAttributes;

            return overrideTranslucentMaterials
                || (!material.IsTranslucent && attributes.GetValueOrDefault("IgnoreMaterialOverride") == 0)
                || attributes.GetValueOrDefault("ForceMaterialOverride") != 0;
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
        /// Model space to world. The model takes the local offset, less the centre of its mesh bounds when
        /// centred, then the scale, the authored local rotation and the particle's own angles, and is then
        /// turned into the frame its normal gives, unless the normal is ignored. A local scale applies
        /// along the model's own axes, otherwise it applies after the turns, along the axes of that frame.
        /// </summary>
        private Matrix4x4 ParticleTransform(ref Particle particle, ParticleSystemState systemState, ModelSceneNode node, Vector3 scale, float roll)
        {
            var angles = particle.Rotation;
            var turn = EntityTransformHelper.EulerAnglesToRotationMatrix(localRotation.NextVector(ref particle, systemState))
                * EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(angles.Y, angles.X, angles.Z * roll));

            var scaling = Matrix4x4.CreateScale(scale);
            var basis = ignoreNormal ? Matrix4x4.Identity : NormalBasis(particle.Normal, orientZ);
            var model = localScale ? scaling * turn * basis : turn * scaling * basis;

            var offset = localOffset.NextVector(ref particle, systemState);

            if (centerOffset)
            {
                offset -= node.LocalBoundingBox.Center;
            }

            return Matrix4x4.CreateTranslation(offset) * model * Matrix4x4.CreateTranslation(particle.Position);
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
            perpendicular = MathUtils.SafeNormalize(MathUtils.ProjectOntoPlane(perpendicular, direction));
            var third = Vector3.Cross(direction, perpendicular);

            return orientZ
                ? MathUtils.CreateBasis(third, -perpendicular, direction)
                : MathUtils.CreateBasis(direction, perpendicular, third);
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
