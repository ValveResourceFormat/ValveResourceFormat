using System.Buffers;
using System.Linq;
using System.Runtime.InteropServices;
using GUI.Types.Renderer.Buffers;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace GUI.Types.Renderer
{
    class ModelSceneNode : MeshCollectionNode
    {
        public Vector4 Tint
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

        public readonly AnimationController AnimationController;
        public string ActiveMaterialGroup => activeMaterialGroup.Name;
        public bool HasMeshes => meshRenderers.Count > 0;

        private readonly List<RenderableMesh> meshRenderers = [];
        private readonly List<Animation> animations = [];

        public bool IsAnimated => boneMatricesGpu != null;
        private StorageBuffer boneMatricesGpu;
        private readonly int boneCount;
        private readonly int[] remappingTable;

        private HashSet<string> activeMeshGroups = [];
        private (string Name, string[] Materials) activeMaterialGroup;
        private Dictionary<string, string> materialTable;

        private readonly (string Name, string[] Materials)[] materialGroups;
        private readonly string[] meshGroups;
        private readonly ulong[] meshGroupMasks;
        private readonly List<(int MeshIndex, string MeshName, long LoDMask)> meshNamesForLod1;

        public ModelSceneNode(Scene scene, Model model, string skin = null, bool isWorldPreview = false)
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

            if (skin != null)
            {
                SetMaterialGroup(skin);
            }

            LoadMeshes(model);
            UpdateBoundingBox();
            LoadAnimations(model, embededAnimationsOnly: isWorldPreview);

            SetCharacterEyeRenderParams();
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

        public int GetMeshBoneIndex(int modelBoneIndex, RenderableMesh mesh)
            => remappingTable.AsSpan(mesh.MeshBoneOffset, mesh.MeshBoneCount).IndexOf(modelBoneIndex);

        public override void Update(Scene.UpdateContext context)
        {
            if (!AnimationController.Update(context.Timestep))
            {
                return;
            }

            if (IsAnimated)
            {
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

        public override IEnumerable<string> GetSupportedRenderModes()
            => meshRenderers.SelectMany(renderer => renderer.GetSupportedRenderModes()).Distinct();

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

        private void LoadAnimations(Model model, bool embededAnimationsOnly)
        {
            animations.AddRange(embededAnimationsOnly
                ? model.GetEmbeddedAnimations()
                : model.GetAllAnimations(Scene.GuiContext.FileLoader)
            );

            if (animations.Count != 0)
            {
                SetupBoneMatrixBuffers();
            }
        }

        private void LoadMeshes(Model model)
        {
            // Get embedded meshes
            foreach (var embeddedMesh in model.GetEmbeddedMeshesAndLoD().Where(m => (m.LoDMask & 1) != 0))
            {
                embeddedMesh.Mesh.LoadExternalMorphData(Scene.GuiContext.FileLoader);
                model.SetExternalMorphData(embeddedMesh.Mesh.MorphData);

                meshRenderers.Add(new RenderableMesh(embeddedMesh.Mesh, embeddedMesh.MeshIndex, Scene, model, materialTable, embeddedMesh.Mesh.MorphData));
            }

            // Load referred meshes from file (only load meshes with LoD 1)
            foreach (var refMesh in GetLod1RefMeshes())
            {
                var newResource = Scene.GuiContext.LoadFileCompiled(refMesh.MeshName);
                if (newResource == null)
                {
                    continue;
                }

                var mesh = (Mesh)newResource.DataBlock;
                mesh.LoadExternalMorphData(Scene.GuiContext.FileLoader);
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

            boneMatricesGpu = new StorageBuffer(ReservedBufferSlots.Transforms);
        }

        public IEnumerable<string> GetSupportedAnimationNames()
            => animations.Select(a => a.Name);

        public void SetAnimation(string animationName)
        {
            var activeAnimation = animations.FirstOrDefault(a => a.Name == animationName);
            SetAnimation(activeAnimation);
        }

        public bool SetAnimationForWorldPreview(string animationName)
        {
            Animation activeAnimation = null;

            if (animationName != null)
            {
                activeAnimation = animations.FirstOrDefault(a => a.Name == animationName || (a.Name[0] == '@' && a.Name.AsSpan()[1..] == animationName));
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

        public void SetAnimation(Animation activeAnimation)
        {
            AnimationController.SetAnimation(activeAnimation);
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

        public IEnumerable<(int MeshIndex, string MeshName, long LoDMask)> GetLod1RefMeshes()
            => meshNamesForLod1;

        public IEnumerable<string> GetMeshGroups()
            => meshGroups;

        public ICollection<string> GetActiveMeshGroups()
            => activeMeshGroups;

        private IEnumerable<bool> GetActiveMeshMaskForGroup(string groupName)
        {
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

        public void SetActiveMeshGroups(IEnumerable<string> setMeshGroups)
        {
            activeMeshGroups = new HashSet<string>(meshGroups.Intersect(setMeshGroups));

            if (meshGroups.Length > 1)
            {
                RenderableMeshes.Clear();
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
                RenderableMeshes = new List<RenderableMesh>(meshRenderers);
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
        public override void UpdateVertexArrayObjects()
        {
            foreach (var renderer in meshRenderers)
            {
                renderer.UpdateVertexArrayObjects();
            }
        }
#endif
    }
}
