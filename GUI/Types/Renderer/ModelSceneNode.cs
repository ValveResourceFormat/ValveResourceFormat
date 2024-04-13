using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

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
        public List<RenderableMesh> RenderableMeshes => activeMeshRenderers;
        public string ActiveMaterialGroup => activeMaterialGroup.Name;

        private readonly List<RenderableMesh> meshRenderers = [];
        private readonly List<Animation> animations = [];

        public bool IsAnimated => animationTexture != null;
        private RenderTexture animationTexture;
        private readonly int bonesCount;

        private HashSet<string> activeMeshGroups = [];
        private List<RenderableMesh> activeMeshRenderers = [];
        private (string Name, string[] Materials) activeMaterialGroup;
        private Dictionary<string, string> materialTable;

        private readonly (string Name, string[] Materials)[] materialGroups;
        private readonly string[] meshGroups;
        private readonly ulong[] meshGroupMasks;
        private readonly List<(int MeshIndex, string MeshName, long LoDMask)> meshNamesForLod1;

        public ModelSceneNode(Scene scene, Model model, string skin = null, bool optimizeForMapLoad = false)
            : base(scene)
        {
            materialGroups = model.GetMaterialGroups().ToArray();
            meshGroups = model.GetMeshGroups().ToArray();

            if (meshGroups.Length > 1)
            {
                meshGroupMasks = model.Data.GetUnsignedIntegerArray("m_refMeshGroupMasks");
            }

            meshNamesForLod1 = model.GetReferenceMeshNamesAndLoD().Where(m => (m.LoDMask & 1) != 0).ToList();

            if (optimizeForMapLoad)
            {
                model.SetSkeletonFilteredForLod0();
            }

            AnimationController = new(model.Skeleton, model.FlexControllers);
            bonesCount = model.Skeleton.Bones.Length;

            if (skin != null)
            {
                SetMaterialGroup(skin);
            }

            LoadMeshes(model);
            UpdateBoundingBox();
            LoadAnimations(model);
        }

        public Matrix4x4[] EyeBones { get; set; } = new Matrix4x4[3];

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

                var floatBuffer = ArrayPool<float>.Shared.Rent(animationTexture.Height * 16);
                var matrices = MemoryMarshal.Cast<float, Matrix4x4>(floatBuffer);

                UpdateBoundingBox(); // Reset back to the mesh bbox
                var newBoundingBox = LocalBoundingBox;

                try
                {
                    AnimationController.GetBoneMatrices(matrices);
                    if (matrices.Length > 36)
                    {
                        EyeBones[0] = matrices[33];
                        EyeBones[1] = matrices[34];
                        EyeBones[2] = matrices[35];
                    }

                    Animation.GetAnimationMatrices(matrices, frame, AnimationController.FrameCache.Skeleton);

                    // Update animation texture
                    GL.TextureSubImage2D(animationTexture.Handle, 0, 0, 0, animationTexture.Width, animationTexture.Height, PixelFormat.Rgba, PixelType.Float, floatBuffer);

                    var first = true;
                    foreach (var matrix in matrices)
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
            foreach (var renderableMesh in activeMeshRenderers)
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
            foreach (var renderer in meshRenderers)
            {
                renderer.SetRenderMode(renderMode);
            }
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
            if (bonesCount == 0 || animationTexture != null)
            {
                return;
            }

            // Create animation texture
            animationTexture = new(TextureTarget.Texture2D, 4, bonesCount, 1, 1);

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
                activeAnimation = animations.FirstOrDefault(a => a.Name == animationName || (a.Name[0] == '@' && a.Name[1..] == animationName));
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
                activeMeshRenderers.Clear();
                foreach (var group in activeMeshGroups)
                {
                    var meshMask = GetActiveMeshMaskForGroup(group).ToArray();

                    foreach (var meshRenderer in meshRenderers)
                    {
                        if (meshMask[meshRenderer.MeshIndex])
                        {
                            activeMeshRenderers.Add(meshRenderer);
                        }
                    }
                }
            }
            else
            {
                activeMeshRenderers = new List<RenderableMesh>(meshRenderers);
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
