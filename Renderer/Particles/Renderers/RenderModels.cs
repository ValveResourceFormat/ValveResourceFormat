using System.Globalization;
using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
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
    /// Each particle picks its model's mesh groups and, when animated, the sequence and the point in it
    /// it is posed at. Cloth and material variables are not applied. Models that should draw only into
    /// the effects bloom or water effects pass are not drawn at all.
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
        private readonly INumberProvider renderFilter = new LiteralNumberProvider(1f);
        private readonly int lod = -1;

        private readonly ParticleField bodyGroupField = ParticleField.SequenceNumber;
        private readonly ParticleField subModelField = ParticleField.ScratchFloat;
        private readonly RenderModelSubModelFieldType subModelFieldType;

        private readonly bool animated;

        /// <summary>Whether models are posed from their sequence even when not animated.</summary>
        private readonly bool posedWhenNotAnimated;

        /// <summary>
        /// Whether a particle was ever drawn below full alpha, which poses models from their sequence from
        /// then on.
        /// </summary>
        /// <remarks>Renderers are not reset when the system restarts, so this stays set across a restart.</remarks>
        private bool posedForAlpha;

        private readonly INumberProvider animationRate = new LiteralNumberProvider(30f);
        private readonly bool scaleAnimationRate;
        private readonly bool forceLoopingAnimation;
        private readonly bool resetAnimationOnStop;
        private readonly bool manualAnimationFrame;
        private readonly ParticleField animationScaleField = ParticleField.TrailLength;
        private readonly ParticleField animationField = ParticleField.SecondSequenceNumber;
        private readonly ParticleField manualFrameField = ParticleField.CreationTime;
        private readonly string sequenceName;

        /// <summary>The dynamic combo <c>m_szRenderAttribute</c> sets on the model's materials, or null for none.</summary>
        private readonly (string Name, byte Value)? renderAttributeCombo;

        /// <summary>What each node shows beyond its placement, kept with the node while it waits to be reused.</summary>
        private readonly Dictionary<ModelSceneNode, NodeState> nodeStates = [];

        private sealed class NodeState(ulong meshGroupMask)
        {
            /// <summary>The mesh groups the node draws, bit N for group N.</summary>
            public ulong MeshGroupMask { get; set; } = meshGroupMask;

            /// <summary>The sequence the node plays, or null when it is posed by nothing.</summary>
            public AnimationBase? Sequence { get; set; }
        }

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
            renderFilter = parse.NumberProvider("m_flRenderFilter", renderFilter);
            lod = parse.Int32("m_nLOD", lod);

            bodyGroupField = parse.ParticleField("m_nBodyGroupField", bodyGroupField);
            subModelField = parse.ParticleField("m_nSubModelField", subModelField);
            subModelFieldType = parse.Enum("m_nSubModelFieldType", subModelFieldType);

            animated = parse.Boolean("m_bAnimated", animated);
            animationRate = parse.NumberProvider("m_flAnimationRate", animationRate);
            scaleAnimationRate = parse.Boolean("m_bScaleAnimationRate", scaleAnimationRate);
            forceLoopingAnimation = parse.Boolean("m_bForceLoopingAnimation", forceLoopingAnimation);
            resetAnimationOnStop = parse.Boolean("m_bResetAnimOnStop", resetAnimationOnStop);
            manualAnimationFrame = parse.Boolean("m_bManualAnimFrame", manualAnimationFrame);
            animationScaleField = parse.ParticleField("m_nAnimationScaleField", animationScaleField);
            animationField = parse.ParticleField("m_nAnimationField", animationField);
            manualFrameField = parse.ParticleField("m_nManualFrameField", manualFrameField);
            sequenceName = parse.String("m_SequenceName");

            // An activity (m_ActivityName) is not resolved to a sequence, so a model without a named sequence
            // plays the sequence its animation attribute picks.
            var doNotDrawInParticlePass = parse.Boolean("m_bDoNotDrawInParticlePass", false);
            var drawInterleaved = parse.Boolean("m_bForceDrawInterlevedWithSiblings", false);

            // Without a model that can be animated or a model input, nothing is posed or drawn interleaved;
            // otherwise a translucent model is always drawn interleaved with the renderer's siblings.
            // TODO: A named value model input counts as well once its name is set, but those inputs are not read
            if (modelInputControlPoint < 0 && !models.Any(entry => entry.Model != null && CanAnimate(entry.Model)))
            {
                animated = false;
                drawInterleaved = false;
            }
            else if (!doNotDrawInParticlePass && models.Any(static entry => entry.Model != null && IsTranslucent(entry.Model)))
            {
                drawInterleaved = true;
            }

            posedWhenNotAnimated = !doNotDrawInParticlePass
                && (drawInterleaved || OnlyRenderInEffectsBloomPass || OnlyRenderInEffectsWaterPass || ScalesWithControlPointView(parse));

            renderAttributeCombo = ParseRenderAttributeCombo(parse.String("m_szRenderAttribute"));

            // The nodes have no way to stop casting shadows, so m_bDisableShadows is not applied. Taking
            // decals (m_bAcceptsDecals) is left to the host, and m_bAllowApproximateTransforms only saves
            // updating a model that barely moved.
            // TODO: m_MaterialVars set dynamic material attributes per particle, by attribute name. The model
            // materials are shared between nodes and keep their values by parameter name, so there is
            // nowhere to put a value for one particle.
        }

        /// <summary>
        /// Reads a render attribute written as a name and a whole number, keeping it only when it names a
        /// dynamic combo.
        /// </summary>
        private static (string Name, byte Value)? ParseRenderAttributeCombo(string renderAttribute)
        {
            var parts = renderAttribute.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 || !parts[0].StartsWith("D_", StringComparison.Ordinal))
            {
                return null;
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            return (parts[0], (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue));
        }

        private const int ModelFlagTranslucent = 0x1;
        private const int ModelFlagTranslucentTwoPass = 0x2;
        private const int ModelFlagHasSkinnedMeshes = 0x400;
        private const int ModelFlagAnimationDrivenFlexes = 0x200000;

        private static int ModelInfoFlags(Model model)
            => model.Data.GetSubCollection("m_modelInfo") is { } modelInfo ? modelInfo.GetInt32Property("m_nFlags") : 0;

        private static bool IsTranslucent(Model model)
            => (ModelInfoFlags(model) & (ModelFlagTranslucent | ModelFlagTranslucentTwoPass)) != 0;

        /// <summary>Whether the model has skinned meshes or animation driven flexes, and sequences to play.</summary>
        private bool CanAnimate(Model model)
            => (ModelInfoFlags(model) & (ModelFlagHasSkinnedMeshes | ModelFlagAnimationDrivenFlexes)) != 0
                && ModelElements.SequenceNames(model, fileLoader).Length > 0;

        /// <summary>Whether the draw is scaled by how the camera sees a control point, or by the field of view.</summary>
        private static bool ScalesWithControlPointView(ParticleDefinitionParser parse)
        {
            if (parse.Data.GetSubCollection("VisibilityInputs") is not { } visibilityInputs)
            {
                return false;
            }

            var visibility = parse.Nested(visibilityInputs);

            return visibility.Int32("m_nCPin", -1) >= 0 || visibility.Float("m_flRadiusScaleFOVBase", 0f) > 0f;
        }

        /// <summary>
        /// Whether any of the first four particles is drawn below full alpha, with its alpha, its
        /// <see cref="ParticleFunctionRenderer.Alpha2Field"/> value and the alpha scale combined. Only those
        /// four are tested.
        /// </summary>
        private static bool FirstBlockBelowFullAlpha(ReadOnlySpan<float> alphas, ReadOnlySpan<float> alpha2s, float alphaScale)
        {
            var count = Math.Min(alphas.Length, 4);

            for (var i = 0; i < count; i++)
            {
                if (alphas[i] * alpha2s[i] * alphaScale < 1f)
                {
                    return true;
                }
            }

            return false;
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
            var renderFilters = renderFilter.Fill(current, systemState);

            var posed = animated || posedWhenNotAnimated || posedForAlpha;

            // A particle below full alpha poses the models from the next frame on
            if (!posedForAlpha && FirstBlockBelowFullAlpha(alphas, alpha2s, scales.Alpha))
            {
                posedForAlpha = true;
            }
            var animation = posed
                ? new AnimationColumns(current, animationField, animationScaleField, manualFrameField)
                : default;
            var animationRates = posed ? animationRate.Fill(current, systemState) : default;
            var subModels = new SubModelColumns(current, bodyGroupField, subModelField);
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

                    // Vector inputs drawing per block of four draw only for the blocks with a model drawn, in this order,
                    // all in one pass rather than in parallel chunks of particles
                    if (i >= nextInputBlock)
                    {
                        var (blockOffset, block) = current.BlockAt(i);
                        var lane = block.Start & 3;
                        localRotation.FillPerBlock(block, lane, systemState, localRotations[blockOffset..]);
                        componentScale.FillPerBlock(block, lane, systemState, componentScales[blockOffset..]);
                        localOffset.FillPerBlock(block, lane, systemState, localOffsets[blockOffset..]);
                        nextInputBlock = blockOffset + block.Length;
                    }

                    var rotation = localRotations[i];
                    var scale = componentScales[i] * controlPointScale
                        * (ignoreRadius ? 1f : MathF.Max(ParticleMath.FloatEpsilon, radii[i] * scales.Radius));
                    var offset = localOffsets[i];

                    // The alpha scale applies twice: once with the particle's alpha and again in the blend
                    var alpha = BlendColor(colors[i], alphas[i], alpha2s[i], scales, scales.Alpha).W;

                    // The tint is truncated to bytes without a clamp, so a channel past 1 wraps around
                    var tintBytes = ParticleColorBlend.ApplyRounded(ColorBlendType, ParticleColorBlend.ToWrappedBytes(colors[i], 0f), scales.Color);

                    node.Transform = ParticleTransform(i, columns, node, rotation, scale, offset, scales.Roll);
                    node.Tint = suppressTint ? Vector3.One : tintBytes.AsVector3() / 255f;
                    node.Alpha = alpha;
                    node.Visible = renderFilters[i] >= 1f;

                    var nodeModel = models[entry.Model].Model!;
                    var state = nodeStates[node];

                    SelectMeshGroups(node, state, nodeModel.MeshGroups, subModels, i);

                    if (posed)
                    {
                        var (time, rate) = PlaybackTimeAndRate(current, systemState, animation, animationRates[i], i);
                        Animate(node, state, FindSequence(node, nodeModel, animation, i), time, rate);
                    }

                    if (systemState.ModelParticleControlPoints.Count > 0)
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
        public override void Hide(ParticleCollection particles)
        {
            if (pool.Column is { } column)
            {
                pool.ReleaseDead(particles, column, releaseNode);
            }

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

            var nodeModel = models[model].Model!;
            var node = new ModelSceneNode(scene, nodeModel, isWorldPreview: true);
            ApplyOverrideMaterial(node);
            ApplyRenderAttributeCombo(node);

            if (lod >= 0)
            {
                node.SetOverrideLod(lod);
            }

            // A posed particle sets the playback time itself every frame, and a node that is not posed
            // plays nothing, so the node's own clock never advances
            node.AnimationController.FrametimeMultiplier = 0f;

            nodeStates[node] = new NodeState(nodeModel.MeshGroups.DefaultMask);
            scene.Add(node, true);

            return (node, model, 0);
        }

        /// <summary>Sets the render attribute's dynamic combo on every material of the node whose shader has it.</summary>
        private void ApplyRenderAttributeCombo(ModelSceneNode node)
        {
            if (renderAttributeCombo is not var (combo, value))
            {
                return;
            }

            foreach (var mesh in node.AllRenderableMeshes)
            {
                foreach (var drawCall in mesh.DrawCalls)
                {
                    var material = drawCall.Material;

                    if (!material.Shader.DeclaresCombo(combo))
                    {
                        continue;
                    }

                    var staticArguments = material.Material.GetShaderArguments();
                    var arguments = new Dictionary<string, byte>(material.Shader.Parameters.Except(staticArguments))
                    {
                        [combo] = value,
                    };

                    drawCall.SetNewMaterial(scene.RendererContext.MaterialLoader.GetMaterial(material.Material.Name, arguments));
                }
            }
        }

        /// <summary>
        /// Picks the mesh groups the particle's model draws from its sub-model attribute, starting from the
        /// model's defaults: a choice of the body group its body group attribute names, a single mesh group
        /// in place of the others of its body group, or a whole mask.
        /// </summary>
        private void SelectMeshGroups(ModelSceneNode node, NodeState state, ModelMeshGroups meshGroups, in SubModelColumns columns, int index)
        {
            var subModel = columns.SubModels.IsEmpty ? 1f : columns.SubModels[index];
            var mask = meshGroups.DefaultMask;

            switch (subModelFieldType)
            {
                case RenderModelSubModelFieldType.SUBMODEL_AS_BODYGROUP_SUBMODEL:
                    if (!columns.BodyGroups.IsEmpty)
                    {
                        mask = WithBodyGroupChoice(mask, meshGroups, (int)columns.BodyGroups[index], (int)subModel);
                    }

                    break;

                case RenderModelSubModelFieldType.SUBMODEL_AS_MESHGROUP_INDEX:
                    mask = WithMeshGroup(mask, meshGroups, (int)subModel);
                    break;

                case RenderModelSubModelFieldType.SUBMODEL_AS_MESHGROUP_MASK:
                    mask = (ulong)subModel;
                    break;
            }

            if (mask == state.MeshGroupMask)
            {
                return;
            }

            state.MeshGroupMask = mask;

            var names = new List<string>();

            for (var group = 0; group < meshGroups.Names.Count && group < 64; group++)
            {
                if ((mask & 1UL << group) != 0)
                {
                    names.Add(meshGroups.Names[group]);
                }
            }

            node.SetActiveMeshGroups(names);
        }

        /// <summary>The mask with the choice of a body group drawn in place of the others of that group, or as it was when either is out of range.</summary>
        private static ulong WithBodyGroupChoice(ulong mask, ModelMeshGroups meshGroups, int bodyGroup, int choice)
        {
            if ((uint)bodyGroup >= (uint)meshGroups.BodyGroups.Count)
            {
                return mask;
            }

            var group = meshGroups.BodyGroups[bodyGroup];

            if ((uint)choice >= (uint)group.Choices.Count)
            {
                return mask;
            }

            return WithChoice(mask, group, group.Choices[choice].GroupIndex);
        }

        /// <summary>The mask with a mesh group drawn in place of the others of its body group, or as it was when no body group holds it.</summary>
        private static ulong WithMeshGroup(ulong mask, ModelMeshGroups meshGroups, int group)
        {
            if ((uint)group >= (uint)meshGroups.Names.Count)
            {
                return mask;
            }

            foreach (var bodyGroup in meshGroups.BodyGroups)
            {
                foreach (var choice in bodyGroup.Choices)
                {
                    if (choice.GroupIndex == group)
                    {
                        return WithChoice(mask, bodyGroup, group);
                    }
                }
            }

            return mask;
        }

        /// <summary>The mask with one mesh group of a body group drawn and the body group's other choices not.</summary>
        private static ulong WithChoice(ulong mask, BodyGroup bodyGroup, int group)
        {
            foreach (var choice in bodyGroup.Choices)
            {
                if (choice.GroupIndex < 64)
                {
                    mask &= ~(1UL << choice.GroupIndex);
                }
            }

            if (group < 64)
            {
                mask |= 1UL << group;
            }

            return mask;
        }

        /// <summary>
        /// The time and rate the particle plays its sequence at: the manual frame attribute at a rate of one
        /// when the frame is set manually, otherwise its age at the animation rate, scaled by the rate scale
        /// attribute when enabled. Once the system enters its endcap, a renderer that resets its animation on
        /// stop plays every particle from the endcap's start instead, which a stop without an endcap leaves
        /// unset, so the time is then far past any sequence's end.
        /// </summary>
        private (float Time, float Rate) PlaybackTimeAndRate(ParticleSpan particles, ParticleSystemState systemState, in AnimationColumns columns, float rate, int index)
        {
            if (manualAnimationFrame && !columns.ManualFrames.IsEmpty)
            {
                return (columns.ManualFrames[index], 1f);
            }

            if (scaleAnimationRate && !columns.RateScales.IsEmpty)
            {
                rate *= columns.RateScales[index];
            }

            if (resetAnimationOnStop && systemState.InEndCap)
            {
                return (systemState.Age - systemState.EndCapStartAge, rate);
            }

            return (particles.Time - columns.CreationTimes[index], rate);
        }

        /// <summary>
        /// Gives the particle's model the sequence the particle plays and poses it at the point its time and
        /// rate reach. The rate counts frames per second, so the point is the frames played over the frame
        /// intervals of one cycle. A looping sequence, or any sequence when looping is forced, keeps the
        /// fraction of that past its whole cycles; any other holds its first and last frames.
        /// </summary>
        private void Animate(ModelSceneNode node, NodeState state, AnimationBase? sequence, float time, float rate)
        {
            if (sequence != state.Sequence)
            {
                state.Sequence = sequence;
                node.SetAnimation(sequence);
            }

            if (sequence == null)
            {
                return;
            }

            var intervals = sequence.CycleFrames;
            var cycle = intervals > 0 ? time * rate / intervals : 0f;

            if (forceLoopingAnimation || sequence is SequenceAnimation { IsLooping: true })
            {
                cycle -= MathF.Truncate(cycle);

                // A negative time or rate leaves the cycle below zero, which poses the first frame, as does a
                // cycle that is not a number
                if (cycle < 0f || float.IsNaN(cycle))
                {
                    cycle = 0f;
                }
            }
            else
            {
                cycle = ParticleMath.ClampToUnit(cycle);
            }

            node.AnimationController.Time = cycle * sequence.CycleDuration;
        }

        /// <summary>
        /// The sequence the particle plays: the named one when the model has it, otherwise the one its
        /// animation attribute numbers, or the first when the attribute is unset or out of range, or null
        /// when that sequence is not loaded.
        /// </summary>
        private AnimationBase? FindSequence(ModelSceneNode node, Model model, in AnimationColumns columns, int index)
        {
            if (sequenceName.Length > 0 && node.Animations.TryGetValue(sequenceName, out var named))
            {
                return named;
            }

            // Only the sequences the model embeds are loaded, so those of the animation groups it references stay unposed
            var names = ModelElements.SequenceNames(model, fileLoader);

            if (names.Length == 0)
            {
                return null;
            }

            var sequence = columns.Sequences.IsEmpty ? -1 : (int)columns.Sequences[index];

            if ((uint)sequence >= (uint)names.Length)
            {
                sequence = 0;
            }

            return node.Animations.GetValueOrDefault(names[sequence]);
        }

        /// <summary>Reads <paramref name="field"/> when it is a scalar attribute the renderer may use, else nothing.</summary>
        private static ReadOnlySpan<float> ReadOptionalField(ParticleSpan particles, ParticleField field)
            => field != ParticleField.NoneDisabled && ParticleBuffer.IsScalar(field) ? particles.ReadFloats(field) : default;

        /// <summary>The attributes the mesh groups are picked by, for every particle of a span.</summary>
        private readonly ref struct SubModelColumns
        {
            public SubModelColumns(ParticleSpan particles, ParticleField bodyGroupField, ParticleField subModelField)
            {
                BodyGroups = ReadOptionalField(particles, bodyGroupField);
                SubModels = ReadOptionalField(particles, subModelField);
            }

            /// <summary>The body group each particle picks a choice of, or empty without a usable attribute.</summary>
            public ReadOnlySpan<float> BodyGroups { get; }

            /// <summary>The sub-model value of each particle, or empty without a usable attribute, in which case it reads as one.</summary>
            public ReadOnlySpan<float> SubModels { get; }
        }

        /// <summary>The attributes and inputs a model is animated by, for every particle of a span.</summary>
        private readonly ref struct AnimationColumns
        {
            public AnimationColumns(ParticleSpan particles, ParticleField sequenceField, ParticleField rateScaleField, ParticleField manualFrameField)
            {
                Sequences = ReadOptionalField(particles, sequenceField);
                RateScales = ReadOptionalField(particles, rateScaleField);
                ManualFrames = ReadOptionalField(particles, manualFrameField);
                CreationTimes = particles.ReadFloats(ParticleField.CreationTime);
            }

            /// <summary>The sequence each particle plays, or empty without a usable attribute.</summary>
            public ReadOnlySpan<float> Sequences { get; }

            /// <summary>What each particle's animation rate is scaled by, or empty without a usable attribute.</summary>
            public ReadOnlySpan<float> RateScales { get; }

            /// <summary>The frame each particle is posed at when set manually, or empty without a usable attribute.</summary>
            public ReadOnlySpan<float> ManualFrames { get; }

            public ReadOnlySpan<float> CreationTimes { get; }
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
            nodeStates.Clear();
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
