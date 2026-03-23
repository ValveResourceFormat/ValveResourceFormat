using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node for rendering animated models with skeletal animation and morph targets.
    /// </summary>
    public class ModelSceneNode : MeshCollectionNode
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

        /// <summary>Gets the name of the currently active material group (skin).</summary>
        public string ActiveMaterialGroup => activeMaterialGroup.Name;

        /// <summary>Gets whether this model has at least one mesh renderer loaded.</summary>
        public bool HasMeshes => meshRenderers.Count > 0;

        private readonly List<RenderableMesh> meshRenderers = [];
        protected readonly List<Animation> Animations = [];

        /// <summary>Gets whether this model has an active GPU bone matrix buffer (i.e., has animations loaded).</summary>
        public bool IsAnimated => boneMatricesGpu != null;
        private StorageBuffer? boneMatricesGpu;
        private readonly int boneCount;
        private readonly int[] remappingTable;

        private HashSet<string> activeMeshGroups = [];
        private (string Name, string[] Materials) activeMaterialGroup;
        private Dictionary<string, string>? materialTable;

        private readonly (string Name, string[] Materials)[] materialGroups;
        private readonly string[] meshGroups;
        private readonly ulong[]? meshGroupMasks;
        private readonly List<(int MeshIndex, string MeshName, long LoDMask)> meshNamesForLod1;

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
            meshGroups = model.GetMeshGroups().ToArray();

            if (meshGroups.Length > 1)
            {
                meshGroupMasks = model.Data.GetUnsignedIntegerArray("m_refMeshGroupMasks");
            }

            meshNamesForLod1 = model.GetReferenceMeshNamesAndLoD().Where(m => (m.LoDMask & 1) != 0).ToList();

            AnimationController = new(model.Skeleton, model.FlexControllers);
            boneCount = model.Skeleton.Bones.Length;
            remappingTable = model.Data.GetIntegerArray("m_remappingTable").Select(i => (int)i).ToArray();

            if (model.Data.GetArray<string>("m_vecNmSkeletonRefs") is { Length: > 0 } nmSkelRefs)
            {
                foreach (var skeletonName in nmSkelRefs)
                {
                    var resource = Scene.RendererContext.FileLoader.LoadFileCompiled(skeletonName);
                    if (resource?.DataBlock is not BinaryKV3 skeletonData)
                    {
                        continue;
                    }

                    var skeleton = Skeleton.FromSkeletonData(skeletonData.Data);
                    AnimationController.RegisterExternalSkeleton(skeletonName, skeleton);
                }

                var animGraphs = model.Data.GetArray("m_animGraph2Refs");

                // just in case there is any recursive or duplicate references
                var visitedResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var animGraphRef in animGraphs)
                {
                    var graphName = animGraphRef.GetProperty<string>("m_hGraph");
                    LoadAnimGraphResources(graphName, visitedResources);
                }
            }

            if (skin != null)
            {
                SetMaterialGroup(skin);
            }

            Name = model.Name;

            LoadMeshes(model);
            UpdateBoundingBox();
            LoadAnimations(model, embeddedAnimationsOnly: isWorldPreview);

            SetCharacterEyeRenderParams();
            AnimationController.TwistConstraints = ParseTwistConstraints(model);
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
        public void SetCharacterEyeRenderParams()
        {
            var eyeEnablingMaterials = meshRenderers
                .SelectMany(Mesh => Mesh.DrawCallsOpaque.Select(Draw => (Mesh, Draw)))
                .Where(meshDraw => meshDraw.Draw.Material.Material.IntParams.GetValueOrDefault("F_EYEBALLS") == 1)
                .Select(meshDraw => (meshDraw.Mesh, meshDraw.Draw.Material.Material))
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

            foreach (var (mesh, materialData) in eyeEnablingMaterials)
            {
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

        /// <summary>
        /// Returns the mesh-local bone index for the given model-level bone index within the specified mesh's remapping table slice.
        /// </summary>
        public int GetMeshBoneIndex(int modelBoneIndex, RenderableMesh mesh)
            => remappingTable.AsSpan(mesh.MeshBoneOffset, mesh.MeshBoneCount).IndexOf(modelBoneIndex);

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!AnimationController.Update(context.Timestep))
            {
                return;
            }

            AnimationController.ApplyConstraints();

            if (IsAnimated)
            {
                Debug.Assert(boneMatricesGpu != null, "boneMatricesGpu should not be null when IsAnimated is true");

                // Update animation matrices
                var meshBoneCount = remappingTable.Length;

                var floatBufferSizeMeshBones = meshBoneCount * 12;
                var floatBufferSizeModelBones = boneCount * 16;

                var floatBuffer = ArrayPool<float>.Shared.Rent(floatBufferSizeMeshBones + floatBufferSizeModelBones);

                var meshBones = MemoryMarshal.Cast<float, OpenTK.Mathematics.Matrix3x4>(floatBuffer.AsSpan(0, floatBufferSizeMeshBones));
                var modelBones = MemoryMarshal.Cast<float, Matrix4x4>(floatBuffer.AsSpan(floatBufferSizeMeshBones));

                UpdateBoundingBox(); // Reset back to the mesh bbox
                var newBoundingBox = LocalBoundingBox;

                try
                {
                    AnimationController.GetSkinningMatrices(modelBones);

                    for (var i = 0; i < meshBoneCount; i++)
                    {
                        var modelBoneIndex = remappingTable[i];
                        var modelBoneExists = modelBoneIndex < boneCount && modelBoneIndex != -1;

                        if (modelBoneExists)
                        {
                            meshBones[i] = modelBones[modelBoneIndex].To3x4();
                        }
                    }

                    boneMatricesGpu.Update(floatBuffer, 0, floatBufferSizeMeshBones * sizeof(float));

                    var first = true;
                    foreach (var matrix in modelBones[..boneCount])
                    {
                        var bbox = LocalBoundingBox.Transform(matrix);
                        newBoundingBox = first ? bbox : newBoundingBox.Union(bbox);
                        first = false;
                    }

                    LocalBoundingBox = newBoundingBox;
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(floatBuffer);
                }
            }

            if (AnimationController.AnimationFrame != null)
            {
                var datas = AnimationController.AnimationFrame.Datas;
                foreach (var renderableMesh in RenderableMeshes)
                {
                    if (renderableMesh.FlexStateManager == null)
                    {
                        continue;
                    }

                    if (renderableMesh.FlexStateManager.SetControllerValues(datas))
                    {
                        renderableMesh.FlexStateManager.UpdateComposite();
                        renderableMesh.FlexStateManager.MorphComposite.Render();
                    }
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
            Animations.AddRange(embeddedAnimationsOnly
                ? model.GetEmbeddedAnimations()
                : model.GetAllAnimations(Scene.RendererContext.FileLoader)
            );

            if (Animations.Count != 0)
            {
                SetupBoneMatrixBuffers();
            }
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

            var clipResource = Scene.RendererContext.FileLoader.LoadFileCompiled(clipName);
            if (clipResource?.DataBlock is not AnimationClip clip)
            {
                return false;
            }

            LoadAnimationClip(clip);
            return true;
        }

        public void LoadAnimationClip(AnimationClip clip)
        {
            var anim = new Animation(clip);
            Animations.Add(anim);
        }

        private bool LoadAnimGraphResources(string graphName, HashSet<string> visited)
        {
            var resource = Scene.RendererContext.FileLoader.LoadFileCompiled(graphName);
            if (resource?.DataBlock is not BinaryKV3 graphData)
            {
                return false;
            }

            var graphResources = graphData.Data.GetArray<string>("m_resources");
            if (graphResources == null)
            {
                return false;
            }

            var clipExt = ResourceType.NmClip.GetExtension()!;
            var graphExt = ResourceType.NmGraph.GetExtension()!;

            foreach (var graphResource in graphResources)
            {
                if (!visited.Add(graphResource))
                {
                    continue;
                }

                if (graphResource.EndsWith(clipExt, StringComparison.OrdinalIgnoreCase))
                {
                    LoadAnimationClip(graphResource);
                }
                else if (graphResource.EndsWith(graphExt, StringComparison.OrdinalIgnoreCase))
                {
                    LoadAnimGraphResources(graphResource, visited);
                }
            }

            return true;
        }

        private void LoadMeshes(Model model)
        {
            // Get embedded meshes
            foreach (var embeddedMesh in model.GetEmbeddedMeshesAndLoD().Where(m => (m.LoDMask & 1) != 0))
            {
                embeddedMesh.Mesh.LoadExternalMorphData(Scene.RendererContext.FileLoader);
                model.SetExternalMorphData(embeddedMesh.Mesh.MorphData);

                meshRenderers.Add(new RenderableMesh(embeddedMesh.Mesh, embeddedMesh.MeshIndex, Scene, model, materialTable, embeddedMesh.Mesh.MorphData));
            }

            // Load referred meshes from file (only load meshes with LoD 1)
            foreach (var refMesh in GetLod1RefMeshes())
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

            // Set active meshes to default
            SetActiveMeshGroups(model.GetDefaultMeshGroups());
        }

        private void SetupBoneMatrixBuffers()
        {
            if (boneCount == 0 || boneMatricesGpu != null)
            {
                return;
            }

            boneMatricesGpu = new StorageBuffer(ReservedBufferSlots.BoneTransforms);
        }

        /// <summary>Returns the names of all animations available on this model.</summary>
        public IEnumerable<string> GetSupportedAnimationNames()
            => Animations.Select(a => a.Name);

        /// <summary>Activates the animation with the given name, or stops animation if not found.</summary>
        public void SetAnimationByName(string animationName)
        {
            var activeAnimation = Animations.FirstOrDefault(a => a.Name == animationName);
            SetAnimation(activeAnimation);
        }

        /// <summary>Activates the animation with the given name with a blend-in time, or stops animation if not found.</summary>
        /// <param name="animationName">The name of the animation to activate.</param>
        /// <param name="blendTime">The time in seconds to blend from the current animation to the new one.</param>
        public void SetAnimationByName(string animationName, float blendTime)
        {
            var activeAnimation = Animations.FirstOrDefault(a => a.Name == animationName);
            SetAnimation(activeAnimation, blendTime);
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
                activeAnimation = Animations.FirstOrDefault(a => a.Name == animationName);
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

        /// <summary>Activates the given animation instance, or clears the active animation when <see langword="null"/>.</summary>
        public void SetAnimation(Animation? activeAnimation)
        {
            SetAnimation(activeAnimation, 0f);
        }

        /// <summary>Activates the given animation instance with a blend-in time, or clears the active animation when <see langword="null"/>.</summary>
        /// <param name="activeAnimation">The animation to activate, or <see langword="null"/> to clear.</param>
        /// <param name="blendTime">The time in seconds to blend from the current animation to the new one.</param>
        public void SetAnimation(Animation? activeAnimation, float blendTime)
        {
            AnimationController.SetAnimation(activeAnimation, blendTime);
            UpdateBoundingBox();

            if (activeAnimation != default)
            {
                foreach (var renderer in meshRenderers)
                {
                    // renderer.SetMaterialCombo(("D_ANIMATED", 1));
                    renderer.SetBoneMatricesBuffer(boneMatricesGpu);
                }
            }
            else
            {
                foreach (var renderer in meshRenderers)
                {
                    // renderer.SetMaterialCombo(("D_ANIMATED", 0));
                    renderer.SetBoneMatricesBuffer(null);
                }
            }
        }

#pragma warning disable CA1024 // Use properties where appropriate
        /// <summary>Returns the external reference mesh names and their LoD masks for LoD level 1.</summary>
        public IEnumerable<(int MeshIndex, string MeshName, long LoDMask)> GetLod1RefMeshes()
            => meshNamesForLod1;

        /// <summary>Returns all mesh group names defined by this model.</summary>
        public IEnumerable<string> GetMeshGroups()
            => meshGroups;

        /// <summary>Returns the set of currently active mesh group names.</summary>
        public ICollection<string> GetActiveMeshGroups()
            => activeMeshGroups;
#pragma warning restore CA1024 // Use properties where appropriate

        private IEnumerable<bool> GetActiveMeshMaskForGroup(string groupName)
        {
            if (meshGroupMasks == null)
            {
                return [];
            }

            var groupIndex = Array.IndexOf(meshGroups, groupName);
            if (groupIndex >= 0)
            {
                return meshGroupMasks.Select(mask => (mask & 1UL << groupIndex) != 0);
            }
            else
            {
                return meshGroupMasks.Select(_ => false);
            }
        }

        /// <summary>
        /// Sets which mesh groups are active, rebuilding the renderable mesh list accordingly.
        /// </summary>
        public void SetActiveMeshGroups(IEnumerable<string> setMeshGroups)
        {
            activeMeshGroups = new HashSet<string>(meshGroups.Intersect(setMeshGroups));

            RenderableMeshes.Clear();

            if (meshGroups.Length > 1)
            {
                foreach (var group in activeMeshGroups)
                {
                    var meshMask = GetActiveMeshMaskForGroup(group).ToArray();

                    foreach (var meshRenderer in meshRenderers)
                    {
                        if (meshMask[meshRenderer.MeshIndex])
                        {
                            RenderableMeshes.Add(meshRenderer);
                        }
                    }
                }
            }
            else
            {
                RenderableMeshes.AddRange(meshRenderers);
            }
        }

        private void UpdateBoundingBox()
        {
            var first = true;
            foreach (var mesh in meshRenderers)
            {
                LocalBoundingBox = first ? mesh.BoundingBox : LocalBoundingBox.Union(mesh.BoundingBox);
                first = false;
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

        /// <summary>
        /// Parses tilt-twist constraints from the model's keyvalues.
        /// </summary>
        protected static TiltTwistConstraint[] ParseTwistConstraints(Model model)
        {
            var keyvalues = model.KeyValues;
            if (!keyvalues.ContainsKey("BoneConstraintList"))
            {
                return [];
            }

            var boneConstraintList = keyvalues.GetArray("BoneConstraintList");
            var constraints = new List<TiltTwistConstraint>();

            foreach (var constraintData in boneConstraintList)
            {
                var className = constraintData.GetStringProperty("_class");
                if (className != "CTiltTwistConstraint")
                {
                    continue;
                }

                var upVec = constraintData.GetFloatArray("m_vUpVector");

                var constraint = new TiltTwistConstraint
                {
                    Name = constraintData.GetStringProperty("m_name"),
                    UpVector = new Vector3(upVec[0], upVec[1], upVec[2]),
                    TargetAxis = (int)constraintData.GetIntegerProperty("m_nTargetAxis"),
                    SlaveAxis = (int)constraintData.GetIntegerProperty("m_nSlaveAxis"),
                };

                // Parse slaves
                var slaves = constraintData.GetArray("m_slaves");
                constraint.Slaves = slaves.Select(s =>
                {
                    var quat = s.GetFloatArray("m_qBaseOrientation");
                    var pos = s.GetFloatArray("m_vBasePosition");

                    return new TiltTwistConstraintSlave
                    {
                        BaseOrientation = new Quaternion(quat[0], quat[1], quat[2], quat[3]),
                        BasePosition = new Vector3(pos[0], pos[1], pos[2]),
                        BoneHash = s.GetUInt32Property("m_nBoneHash"),
                        Weight = s.GetFloatProperty("m_flWeight"),
                        Name = s.GetStringProperty("m_sName"),
                    };
                }).ToArray();

                // Parse targets
                var targets = constraintData.GetArray("m_targets");
                constraint.Targets = targets.Select(t =>
                {
                    var quat = t.GetFloatArray("m_qOffset");
                    var pos = t.GetFloatArray("m_vOffset");

                    return new TiltTwistConstraintTarget
                    {
                        Offset = new Quaternion(quat[0], quat[1], quat[2], quat[3]),
                        PositionOffset = new Vector3(pos[0], pos[1], pos[2]),
                        BoneHash = t.GetUInt32Property("m_nBoneHash"),
                        Name = t.GetStringProperty("m_sName"),
                        Weight = t.GetFloatProperty("m_flWeight"),
                        IsAttachment = t.GetProperty<bool>("m_bIsAttachment"),
                    };
                }).ToArray();

                constraints.Add(constraint);
            }

            return [.. constraints];
        }
    }
}

