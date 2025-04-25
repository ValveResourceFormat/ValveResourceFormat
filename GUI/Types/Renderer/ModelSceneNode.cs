using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace GUI.Types.Renderer
{
    class ModelSceneNode : SceneNode, IRenderableMeshCollection
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
        public List<RenderableMesh> RenderableMeshes { get; private set; } = [];
        public string ActiveMaterialGroup => activeMaterialGroup.Name;

        private readonly List<RenderableMesh> meshRenderers = [];
        private readonly List<Animation> animations = [];

        public bool IsAnimated => animationTexture != null;
        private RenderTexture animationTexture;
        private readonly int boneCount;
        private readonly int[] remappingTable;

        private HashSet<string> activeMeshGroups = [];
        private (string Name, string[] Materials) activeMaterialGroup;
        private Dictionary<string, string> materialTable;

        private readonly (string Name, string[] Materials)[] materialGroups;
        private readonly string[] meshGroups;
        private readonly ulong[] meshGroupMasks;
        private readonly List<(int MeshIndex, string MeshName, long LoDMask)> meshNamesForLod1;

        public ModelSceneNode(Scene scene, Model model, string skin = null)
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
            LoadAnimations(model);

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

                var bonesMatrices = new Matrix4x4[skeleton.Bones.Length];
                animationController.GetBoneMatrices(bonesMatrices, bindPose: true);

                LeftEyePosition = bonesMatrices[LeftEyeBoneIndex].Translation;
                RightEyePosition = bonesMatrices[RightEyeBoneIndex].Translation;
                TargetPosition = bonesMatrices[TargetBoneIndex].Translation;
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

            var frame = AnimationController.GetFrame();

            if (IsAnimated)
            {
                // Update animation matrices
                var meshBoneCount = remappingTable.Length;
                var floatBuffer = ArrayPool<float>.Shared.Rent((meshBoneCount + boneCount) * 16);
                var matrices = MemoryMarshal.Cast<float, Matrix4x4>(floatBuffer);

                UpdateBoundingBox(); // Reset back to the mesh bbox
                var newBoundingBox = LocalBoundingBox;

                try
                {
                    var meshBones = matrices[..meshBoneCount];
                    var modelBones = matrices[meshBoneCount..];
                    var skeleton = AnimationController.FrameCache.Skeleton;
                    Animation.GetAnimationMatrices(modelBones, frame, skeleton);

                    // Copy procedural cloth node transforms from a animated root bone
                    if (skeleton.ClothSimulationRoot is not null)
                    {
                        foreach (var clothNode in skeleton.Roots)
                        {
                            if (clothNode.IsProceduralCloth)
                            {
                                modelBones[clothNode.Index] = modelBones[skeleton.ClothSimulationRoot.Index];
                            }
                        }
                    }

                    for (var i = 0; i < meshBoneCount; i++)
                    {
                        var modelBoneIndex = remappingTable[i];
                        var modelBoneExists = modelBoneIndex < boneCount && modelBoneIndex != -1;

                        if (modelBoneExists)
                        {
                            meshBones[i] = modelBones[modelBoneIndex];
                        }
                    }

                    // Update animation texture
                    GL.TextureSubImage2D(animationTexture.Handle, 0, 0, 0, animationTexture.Width, animationTexture.Height, PixelFormat.Rgba, PixelType.Float, floatBuffer);

                    var first = true;
                    foreach (var matrix in matrices[..boneCount])
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

            //Update morphs
            var datas = frame.Datas;
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

        public override void Render(Scene.RenderContext context)
        {
            // This node does not render itself; it uses the batching system via IRenderableMeshCollection
        }

        public override IEnumerable<string> GetSupportedRenderModes()
            => meshRenderers.SelectMany(renderer => renderer.GetSupportedRenderModes()).Distinct();

        public override void SetRenderMode(string renderMode)
        {
        }

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

        private void LoadAnimations(Model model)
        {
            animations.AddRange(model.GetAllAnimations(Scene.GuiContext.FileLoader));

            if (animations.Count != 0)
            {
                SetupAnimationTextures();
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

                meshRenderers.Add(new RenderableMesh(mesh, refMesh.MeshIndex, Scene, model, materialTable, debugLabel: Path.GetFileName(refMesh.MeshName)));
            }

            // Set active meshes to default
            SetActiveMeshGroups(model.GetDefaultMeshGroups());
        }

        private void SetupAnimationTextures()
        {
            if (boneCount == 0 || animationTexture != null)
            {
                return;
            }

            // Create animation texture
            animationTexture = new(TextureTarget.Texture2D, 4, remappingTable.Length, 1, 1);

#if DEBUG
            var textureName = nameof(animationTexture);
            GL.ObjectLabel(ObjectLabelIdentifier.Texture, animationTexture.Handle, textureName.Length, textureName);
#endif

            // Set clamping to edges
            animationTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
            // Set nearest-neighbor sampling since we don't want to interpolate matrix rows
            animationTexture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

            GL.TextureStorage2D(animationTexture.Handle, 1, SizedInternalFormat.Rgba32f, animationTexture.Width, animationTexture.Height);
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
                    renderer.SetAnimationTexture(animationTexture);
                }
            }
            else
            {
                foreach (var renderer in meshRenderers)
                {
                    renderer.SetAnimationTexture(null);
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
            var groupIndex = meshGroups.ToList().IndexOf(groupName);
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
