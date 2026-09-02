using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node for rendering animated models with skeletal animation and morph targets.
    /// </summary>
    public partial class ModelSceneNode : MeshCollectionNode
    {
        /// <inheritdoc/>
        public override Vector4 Tint
        {
            get
            {
                if (meshRenderers.Count > 0)
                {
                    return meshRenderers[0].Tint;
                }

                return Vector4.One;
            }
            set
            {
                foreach (var renderer in meshRenderers)
                {
                    renderer.Tint = value;
                }
            }
        }

        /// <summary>Gets the animation controller managing skeletal pose and flex data for this model.</summary>
        public AnimationController AnimationController { get; }

        /// <summary>
        /// A collection of animations available for playback on this model.
        /// </summary>
        public Dictionary<string, Animation> Animations { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets the name of the currently active material group (skin).</summary>
        public string ActiveMaterialGroup => activeMaterialGroup.Name;

        /// <summary>Gets whether this model has at least one mesh renderer loaded.</summary>
        public bool HasMeshes => meshRenderers.Count > 0;

        private readonly List<RenderableMesh> meshRenderers = [];

        private (string Name, string[] Materials) activeMaterialGroup;
        private Dictionary<string, string>? materialTable;

        private readonly (string Name, string[] Materials)[] materialGroups;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelSceneNode"/> class and loads its meshes and animations.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="model">The model resource to render.</param>
        /// <param name="skin">The material group (skin) name to activate, or <see langword="null"/> for the default.</param>
        /// <param name="isWorldPreview">When <see langword="true"/>, only embedded animations are loaded.</param>
        public ModelSceneNode(Scene scene, Model model, string? skin = null, bool isWorldPreview = false)
            : base(scene)
        {
            materialGroups = model.GetMaterialGroups().ToArray();
            meshGroups = model.MeshGroups;
            lod = new ModelLodSelector(model.LodInfo);
            referenceMeshes = model.GetReferenceMeshNamesAndLoD().ToList();

            AnimationController = new(model.Skeleton, model.FlexControllers);
            boneCount = model.Skeleton.Bones.Length;
            remappingTable = model.BoneRemapTable.Table;

            if (model.Data.GetArray<string>("m_vecNmSkeletonRefs") is { Length: > 0 } nmSkelRefs)
            {
                foreach (var skeletonName in nmSkelRefs)
                {
                    if (Skeleton.FromSkeletonResource(Scene.RendererContext.FileLoader, skeletonName) is { } skeleton)
                    {
                        AnimationController.RegisterExternalSkeleton(skeletonName, skeleton);
                    }
                }
            }

            if (skin != null)
            {
                SetMaterialGroup(skin);
            }

            Name = model.Name;
            Attachments = model.Attachments;

            LoadMeshes(model);
            UpdateBoundingBox();
            LoadAnimations(model, embeddedAnimationsOnly: isWorldPreview);

            SetCharacterEyeRenderParams();
            Attachments = model.Attachments;
            AnimationController.TwistConstraints = TiltTwistConstraint.ReadList(model);

            dotToMorphConstraints = ParseDotToMorphConstraints(model);
            dotToMorphValues = dotToMorphConstraints.Length > 0
                ? new float[Math.Max(model.FlexControllers.Length, AnimationController.AnimationFrame?.Datas.Length ?? 0)]
                : [];

            // GetAttachmentOrSelfTransform already falls back to this node's own world Transform for an empty/
            // unmatched name - AnimationController.Transform is not it (see its doc comment), so route through here.
            AnimationController.ResolvePosition = attachmentName => GetAttachmentOrSelfTransform(attachmentName).Translation;
        }

        readonly struct CharacterEyeParameters
        {
            public int LeftEyeBoneIndex { get; }
            public Vector3 LeftEyePosition { get; }
            public Vector3 LeftEyeForwardVector { get; } = Vector3.UnitX;
            public Vector3 LeftEyeUpVector { get; } = Vector3.UnitZ;

            public int RightEyeBoneIndex { get; }
            public Vector3 RightEyePosition { get; }
            public Vector3 RightEyeForwardVector { get; } = Vector3.UnitX;
            public Vector3 RightEyeUpVector { get; } = Vector3.UnitZ;

            public int TargetBoneIndex { get; }
            public Vector3 TargetPosition { get; }

            public bool AreValid => LeftEyeBoneIndex != -1 && RightEyeBoneIndex != -1 && TargetBoneIndex != -1;

            public CharacterEyeParameters(AnimationController animationController)
            {
                var skeleton = animationController.FrameCache.Skeleton;

                LeftEyeBoneIndex = skeleton.Bones.FirstOrDefault(b => b.Name == "eyeball_l")?.Index ?? -1;
                RightEyeBoneIndex = skeleton.Bones.FirstOrDefault(b => b.Name == "eyeball_r")?.Index ?? -1;
                TargetBoneIndex = skeleton.Bones.FirstOrDefault(b => b.Name == "eye_target")?.Index ?? -1;

                if (!AreValid)
                {
                    return;
                }

                LeftEyePosition = animationController.BindPose[LeftEyeBoneIndex].Translation;
                RightEyePosition = animationController.BindPose[RightEyeBoneIndex].Translation;
                TargetPosition = animationController.BindPose[TargetBoneIndex].Translation;
            }
        }

        /// <summary>
        /// Detects eye materials on this model and injects bone index and bind-pose uniforms for eyeball rendering.
        /// </summary>
        private void SetCharacterEyeRenderParams()
        {
            var eyeEnablingMaterials = meshRenderers
                .SelectMany(Mesh => Mesh.DrawCallsOpaque.Select(Draw => (Mesh, Draw)))
                .Where(meshDraw => meshDraw.Draw.Material.IntParams.GetValueOrDefault("F_EYEBALLS") == 1)
                .Select(meshDraw => (meshDraw.Mesh, meshDraw.Draw.Material))
                .ToList();

            if (eyeEnablingMaterials.Count == 0)
            {
                return;
            }

            var eyes = new CharacterEyeParameters(AnimationController);

            if (!eyes.AreValid)
            {
                return;
            }

            foreach (var (mesh, material) in eyeEnablingMaterials)
            {
                var materialData = material;

                materialData.IntParams["g_nEyeLBindIdx"] = GetMeshBoneIndex(eyes.LeftEyeBoneIndex, mesh);
                materialData.IntParams["g_nEyeRBindIdx"] = GetMeshBoneIndex(eyes.RightEyeBoneIndex, mesh);
                materialData.IntParams["g_nEyeTargetBindIdx"] = GetMeshBoneIndex(eyes.TargetBoneIndex, mesh);

                materialData.VectorParams["g_vEyeLBindPos"] = new Vector4(eyes.LeftEyePosition, 0);
                materialData.VectorParams["g_vEyeLBindFwd"] = new Vector4(eyes.LeftEyeForwardVector, 0);
                materialData.VectorParams["g_vEyeLBindUp"] = new Vector4(eyes.LeftEyeUpVector, 0);

                materialData.VectorParams["g_vEyeRBindPos"] = new Vector4(eyes.RightEyePosition, 0);
                materialData.VectorParams["g_vEyeRBindFwd"] = new Vector4(eyes.RightEyeForwardVector, 0);
                materialData.VectorParams["g_vEyeRBindUp"] = new Vector4(eyes.RightEyeUpVector, 0);

                materialData.VectorParams["g_vEyeTargetBindPos"] = new Vector4(eyes.TargetPosition, 0);
            }
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            UpdateAutoLod(context.Camera);
            var animationUpdated = AnimationController.Update(context.Timestep);
            UpdateAttachments(context);

            if (!animationUpdated)
            {
                return;
            }

            if (IsAnimated)
            {
                UploadBoneMatrices();
            }

            if (AnimationController.AnimationFrame != null)
            {
                UpdateFlexControllers();
            }
        }

        /// <summary>
        /// Pushes the frame's flex controller values to every mesh that morphs, with the bone driven
        /// morphs layered on top of what the animation supplied.
        /// </summary>
        private void UpdateFlexControllers()
        {
            var datas = AnimationController.AnimationFrame!.Datas;

            foreach (var renderableMesh in RenderableMeshes)
            {
                if (renderableMesh.FlexStateManager == null)
                {
                    continue;
                }

                // A bone driven morph is not in the animation, so it is layered on afterwards.
                if (dotToMorphConstraints.Length > 0)
                {
                    datas.CopyTo(dotToMorphValues, 0);
                    ApplyDotToMorphConstraints(dotToMorphConstraints, dotToMorphValues);
                    datas = dotToMorphValues;
                }

                if (renderableMesh.FlexStateManager.SetControllerValues(datas))
                {
                    renderableMesh.FlexStateManager.UpdateComposite();
                    renderableMesh.FlexStateManager.MorphComposite.Render();
                }
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes()
            => meshRenderers.SelectMany(static renderer => renderer.GetSupportedRenderModes());

        /// <summary>
        /// Activates the named material group (skin), remapping all mesh materials accordingly.
        /// </summary>
        public void SetMaterialGroup(string name)
        {
            if (materialGroups.Length == 0)
            {
                return;
            }

            if (materialTable is null)
            {
                var @default = materialGroups[0];
                activeMaterialGroup = @default;
                materialTable = new(materialGroups[0].Materials.Length);

                if (name == @default.Name)
                {
                    return;
                }
            }

            foreach (var materialGroup in materialGroups)
            {
                if (name == materialGroup.Name)
                {
                    materialTable.Clear();

                    foreach (var (Active, Replacement) in activeMaterialGroup.Materials.Zip(materialGroup.Materials))
                    {
                        materialTable[Active] = Replacement;
                    }

                    activeMaterialGroup = materialGroup;

                    foreach (var mesh in meshRenderers)
                    {
                        mesh.ReplaceMaterials(materialTable);
                    }
                }
            }
        }

        private void LoadAnimations(Model model, bool embeddedAnimationsOnly)
        {
            var animations = (embeddedAnimationsOnly
                ? model.GetEmbeddedAnimations()
                : model.GetAllAnimations(Scene.RendererContext.FileLoader)).ToList();

            animations.RemoveAll(animation => !AnimationController.IsPlayable(animation));

            AddAnimations(animations);

            if (Animations.Count != 0)
            {
                SetupBoneMatrixBuffers();
            }
        }

        /// <summary>
        /// Adds the given animations to the collection of available animations for this model,
        /// prewarming any sound events they can fire so first playback stays allocation-free.
        /// </summary>
        private void AddAnimations(List<Animation> animations)
        {
            Animations.EnsureCapacity(animations.Count);
            foreach (var anim in animations)
            {
                Animations[anim.Name] = anim;
                AnimationPlayer.PrewarmAnimationSounds(anim);
            }
        }

        /// <summary>
        /// Loads an animgraph2 clip from the given <see cref="AnimationClip"/> instance and makes it available for playback on this model.
        /// </summary>
        public void LoadAnimationClip(AnimationClip clip)
        {
            var anim = new ClipAnimation(clip);
            Animations[anim.Name] = anim;
            AnimationPlayer.PrewarmAnimationSounds(anim);
            SetupBoneMatrixBuffers();
        }

        /// <summary>
        /// Loads an animgraph2 clip from the file system and makes it available for playback on this model.
        /// </summary>
        /// <param name="clipName">Clip resource name.</param>
        /// <returns><see langword="true"/> if the clip was found and loaded; otherwise <see langword="false"/>.</returns>
        public bool LoadAnimationClip(string clipName)
        {
            if (!clipName.EndsWith(".vnmclip", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Clip must be a {ResourceType.NmClip} resource.", nameof(clipName));
            }

            if (Animations.ContainsKey(clipName))
            {
                return true;
            }

            var clipResource = Scene.RendererContext.FileLoader.LoadFileCompiled(clipName);
            if (clipResource?.DataBlock is not AnimationClip clip)
            {
                return false;
            }

            LoadAnimationClip(clip);
            return true;
        }

        private void LoadMeshes(Model model)
        {
            // All LoD levels are loaded; the active one is picked at render time.
            foreach (var embeddedMesh in model.GetEmbeddedMeshes())
            {
                embeddedMesh.Mesh.LoadExternalMorphData(Scene.RendererContext.FileLoader);
                model.SetExternalMorphData(embeddedMesh.Mesh.MorphData);

                meshRenderers.Add(new RenderableMesh(embeddedMesh.Mesh, embeddedMesh.MeshIndex, Scene, model, materialTable, embeddedMesh.Mesh.MorphData));
            }

            foreach (var refMesh in referenceMeshes)
            {
                var newResource = Scene.RendererContext.FileLoader.LoadFileCompiled(refMesh.MeshName);
                if (newResource?.DataBlock is not Mesh mesh)
                {
                    continue;
                }

                mesh.LoadExternalMorphData(Scene.RendererContext.FileLoader);
                model.SetExternalMeshData(mesh);

                meshRenderers.Add(new RenderableMesh(mesh, refMesh.MeshIndex, Scene, model, materialTable));
            }

            SetActiveMeshGroups(model.MeshGroups.Defaults);
        }

        /// <summary>Activates the animation with the given name, or stops animation if not found.</summary>
        public void SetAnimationByName(string animationName, float blendTime = 0f, bool warp = false)
        {
            Animations.TryGetValue(animationName, out var activeAnimation);
            SetAnimation(activeAnimation, blendTime, warp);
        }

        /// <summary>
        /// Activates the named animation for world preview mode.
        /// </summary>
        /// <returns><see langword="true"/> if the animation was found and activated; otherwise <see langword="false"/>.</returns>
        public bool SetAnimationForWorldPreview(string animationName)
        {
            Animation? activeAnimation = null;

            if (animationName != null)
            {
                Animations.TryGetValue(animationName, out activeAnimation);
            }

            // TODO: CS2 falls back to the first animation, but other games seemingly do not.
            //activeAnimation ??= animations.FirstOrDefault(); // Fallback to the first animation

            if (activeAnimation != null)
            {
                SetAnimation(activeAnimation);
                return true;
            }

            return false;
        }

        /// <summary>Activates the given animation instance with a blend-in time, or clears the active animation when <see langword="null"/>.</summary>
        /// <param name="activeAnimation">The animation to activate, or <see langword="null"/> to clear.</param>
        /// <param name="blendTime">The time in seconds to blend from the current animation to the new one.</param>
        /// <param name="warp">Whether re-activating the animation already playing should cross over
        /// into a second instance of it rather than restarting it in place.</param>
        public void SetAnimation(Animation? activeAnimation, float blendTime = 0f, bool warp = false)
        {
            AnimationController.SetAnimation(activeAnimation, blendTime, warp);
            UpdateBoundingBox();

            if (activeAnimation != default)
            {
                foreach (var renderer in meshRenderers)
                {
                    renderer.SetBoneMatricesBuffer(boneMatricesGpu);
                }
            }
            else
            {
                foreach (var renderer in meshRenderers)
                {
                    renderer.SetBoneMatricesBuffer(null);
                }
            }
        }



#if DEBUG
        /// <inheritdoc/>
        public override void UpdateVertexArrayObjects()
        {
            foreach (var renderer in meshRenderers)
            {
                renderer.UpdateVertexArrayObjects();
            }
        }
#endif

        /// <inheritdoc/>
        public override void Delete()
        {
            boneMatricesGpu?.Delete();
        }

    }
}
