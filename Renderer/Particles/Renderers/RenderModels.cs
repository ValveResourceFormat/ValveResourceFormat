using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Operators;
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

        /// <summary>How many models the list was authored with, before models from the model input are added.</summary>
        private readonly int authoredModelCount;
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

        /// <summary>
        /// The model each particle was given when it was created, with the node drawing it and that node's
        /// skin, at the slot the particle's handle names less one. A particle no model was picked for, or
        /// that is hidden, has no node.
        /// </summary>
        private readonly ParticleHandlePool<(ModelSceneNode? Node, int Model, int Skin)> pool = new();
        private readonly Action<(ModelSceneNode? Node, int Model, int Skin)> releaseNode;
        private readonly List<Stack<(ModelSceneNode Node, int Skin)>> idle;
        private readonly Dictionary<ModelSceneNode, ParticleModelNodeObject> nodeObjects = [];
        private readonly IFileLoader fileLoader;

        public RenderModels(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse, scene)
        {
            this.scene = scene;
            fileLoader = rendererContext.FileLoader;
            releaseNode = Release;

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

                    var weight = entry.GetFloatProperty("m_flRelativeProbabilityOfSpawn", 1f);
                    models.Add((model, weight, MaterialGroupNames(model)));
                }
            }

            authoredModelCount = models.Count;
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
            if (models.Count == 0 || pool.Column is not { } column)
            {
                return;
            }

            pool.ReleaseDead(particles, column, releaseNode);

            var scales = EvaluateScales(systemState);
            var controlPointScale = modelScaleControlPoint >= 0 ? systemState.GetControlPoint(modelScaleControlPoint).Position : Vector3.One;
            var current = particles.Current;
            var handles = particles.Handles(column);
            var columns = new ModelColumns(current);
            var colors = current.ReadVectors(ParticleField.Color);
            var radii = current.ReadFloats(ParticleField.Radius);
            var alphas = current.ReadFloats(ParticleField.Alpha);
            var alpha2s = ReadAlpha2(current);
            var componentScales = componentScale.Prepare(current, systemState);
            var localOffsets = localOffset.Prepare(current, systemState);
            var localRotations = localRotation.Prepare(current, systemState);
            var skinIndices = skin.Fill(current, systemState);
            var nextInputBlock = 0;

            for (var i = 0; i < current.Length; i++)
            {
                var slot = handles[i] - 1;
                var entry = slot >= 0 ? pool[slot] : (Node: null, Model: -1, Skin: 0);
                var skinIndex = (int)skinIndices[i];

                if (entry.Node == null && entry.Model >= 0)
                {
                    entry = TakeNode(entry.Model);
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

                    // Vector inputs drawing per block of four draw only for the blocks with a model drawn, in this order
                    if (i >= nextInputBlock)
                    {
                        var (blockOffset, block) = current.BlockAt(i);
                        block.FillPerBlockInputs(systemState,
                            localRotation, localRotations.Slice(blockOffset, block.Length),
                            componentScale, componentScales.Slice(blockOffset, block.Length),
                            localOffset, localOffsets.Slice(blockOffset, block.Length));
                        nextInputBlock = blockOffset + block.Length;
                    }

                    var rotation = localRotations[i];
                    var scale = componentScales[i] * controlPointScale
                        * (ignoreRadius ? 1f : MathF.Max(ParticleMath.FloatEpsilon, radii[i] * scales.Radius));
                    var offset = localOffsets[i];

                    // The alpha scale applies twice: once with the particle's alpha and again in the blend
                    var color = BlendColor(colors[i], alphas[i], alpha2s[i], scales, scales.Alpha);

                    node.Transform = ParticleTransform(i, columns, node, rotation, scale, offset, scales.Roll);
                    node.Tint = suppressTint ? Vector3.One : color.AsVector3();
                    node.Alpha = color.W;
                    node.Visible = true;

                    if (systemState.ModelParticleControlPoints.Count > 0 && models[entry.Model].Model is { } nodeModel)
                    {
                        BindControlPoints(particles, systemState, i, node, nodeModel);
                    }
                }

                if (slot >= 0)
                {
                    pool[slot] = entry;
                }
            }
        }

        /// <summary>
        /// Binds the control points <see cref="SetControlPointsToModelParticles"/> sets to the particle at
        /// <paramref name="particleIndex"/> to the model drawn on it, placed on the named attachment where
        /// one is asked for.
        /// </summary>
        private void BindControlPoints(ParticleCollection particles, ParticleSystemState systemState, int particleIndex, ModelSceneNode node, Model model)
        {
            foreach (var binding in systemState.ModelParticleControlPoints)
            {
                var offset = particleIndex - binding.FirstSourcePoint;

                if (offset < 0 || offset >= binding.ActiveCount(particles.Count))
                {
                    continue;
                }

                if (!nodeObjects.TryGetValue(node, out var modelObject))
                {
                    modelObject = new ParticleModelNodeObject(model, node, fileLoader);
                    nodeObjects[node] = modelObject;
                }

                var controlPoint = systemState.GetControlPoint(binding.FirstControlPoint + offset);
                controlPoint.ModelObject = modelObject;

                if (binding.Attachment != null)
                {
                    var attachment = node.GetChildFrame(binding.Attachment);
                    controlPoint.Position = attachment.Translation;
                    controlPoint.SetOrientation(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(attachment)));
                }
            }
        }

        /// <inheritdoc/>
        public override void InitializeParticles(ParticleCollection particles, ParticleSystemState systemState, int first, int count)
        {
            if (models.Count == 0 && modelInputControlPoint < 0)
            {
                return;
            }

            var current = particles.Current;
            var handles = particles.Handles(pool.Claim(particles));

            // The selection is evaluated for every particle before any pick draws. Each new particle reads
            // its own value; reading the value of the particle at its offset among the new ones instead
            // would make a per-particle selection pick from the oldest particles.
            var manualSelections = manualModelSelection.Fill(current, systemState);

            for (var i = first; i < first + count; i++)
            {
                var slot = pool.Allocate((null, PickModel((int)manualSelections[i], systemState), 0));
                handles[i] = slot + 1;
            }
        }

        /// <inheritdoc/>
        public override void Hide()
        {
            // The nodes stay with their particles, to be shown again by the next render
            for (var i = 0; i < pool.Count; i++)
            {
                if (pool[i].Node is { } node)
                {
                    node.Visible = false;
                }
            }
        }

        /// <summary>
        /// The model a new particle draws: the model bound to the model input's control point when there
        /// is one, otherwise the list entry the manual selection names when it is in range, otherwise one
        /// picked by relative probability with one draw from the system's random sequence, or -1 when
        /// there is no model to draw.
        /// </summary>
        private int PickModel(int manual, ParticleSystemState systemState)
        {
            if (systemState.GetControlPointModel(modelInputControlPoint) is { } inputModel)
            {
                return InputModelIndex(inputModel);
            }

            if ((uint)manual < (uint)authoredModelCount)
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
        private Matrix4x4 ParticleTransform(int index, in ModelColumns columns, ModelSceneNode node, Vector3 rotation, Vector3 scale, Vector3 offset, float roll)
        {
            var turn = EntityTransformHelper.EulerAnglesToRotationMatrix(rotation)
                * EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(columns.Pitches[index], columns.Yaws[index], columns.Rolls[index] * roll));

            var scaling = Matrix4x4.CreateScale(scale);
            var basis = ignoreNormal ? Matrix4x4.Identity : NormalBasis(columns.Normals[index], orientZ);
            var model = localScale ? scaling * turn * basis : turn * scaling * basis;

            if (centerOffset)
            {
                offset -= node.LocalBoundingBox.Center;
            }

            return Matrix4x4.CreateTranslation(offset) * model * Matrix4x4.CreateTranslation(columns.Positions[index]);
        }

        /// <summary>The attributes a model is placed by, for every particle of a span.</summary>
        private readonly ref struct ModelColumns(ParticleSpan particles)
        {
            public ReadOnlySpan<Vector3> Positions { get; } = particles.ReadVectors(ParticleField.Position);

            public ReadOnlySpan<Vector3> Normals { get; } = particles.ReadVectors(ParticleField.Normal);

            public ReadOnlySpan<float> Yaws { get; } = particles.ReadFloats(ParticleField.Yaw);

            public ReadOnlySpan<float> Pitches { get; } = particles.ReadFloats(ParticleField.Pitch);

            public ReadOnlySpan<float> Rolls { get; } = particles.ReadFloats(ParticleField.Roll);
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
            var perpendicular = MathUtils.Perpendicular(direction);
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
            foreach (var (node, _, _) in pool.Entries)
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

            pool.Clear();
            nodeObjects.Clear();
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
