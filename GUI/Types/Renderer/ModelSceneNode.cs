using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class ModelSceneNode : SceneNode, IRenderableMeshCollection
    {
        private Model Model { get; }
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

        public AnimationController AnimationController { get; } = new();
        public IEnumerable<RenderableMesh> RenderableMeshes => activeMeshRenderers;

        private readonly List<RenderableMesh> meshRenderers = new();
        private readonly List<Animation> animations = new();
        private Dictionary<string, string> skinMaterials;

        private Animation activeAnimation;
        private int[] animationTextures;
        private Skeleton[] skeletons;

        private ICollection<string> activeMeshGroups = new HashSet<string>();
        private ICollection<RenderableMesh> activeMeshRenderers = new HashSet<RenderableMesh>();

        private AnimationFrameCache animationFrameCache = new AnimationFrameCache();
        private bool loadedAnimations;

        public ModelSceneNode(Scene scene, Model model, string skin = null, bool loadAnimations = true)
            : base(scene)
        {
            Model = model;

            if (skin != null)
            {
                SetSkin(skin);
            }

            LoadMeshes();
            UpdateBoundingBox();

            AnimationController.RegisterUpdateHandler((animation, frame) =>
            {
            });

            // Load required resources
            if (loadAnimations)
            {
                LoadAnimations();
            }
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (activeAnimation == null)
            {
                return;
            }

            AnimationController.Update(context.Timestep);

            for (var i = 0; i < skeletons.Length; i++)
            {
                var skeleton = skeletons[i];
                var animationTexture = animationTextures[i];

                // Update animation matrices
                var animationMatrices = new float[skeleton.AnimationTextureSize * 16];
                for (var j = 0; j < skeleton.AnimationTextureSize; j++)
                {
                    // Default to identity matrices
                    animationMatrices[j * 16] = 1.0f;
                    animationMatrices[(j * 16) + 5] = 1.0f;
                    animationMatrices[(j * 16) + 10] = 1.0f;
                    animationMatrices[(j * 16) + 15] = 1.0f;
                }

                animationMatrices = activeAnimation.GetAnimationMatricesAsArray(animationFrameCache, AnimationController.Time, skeleton);

                // Update animation texture
                GL.BindTexture(TextureTarget.Texture2D, animationTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 4, skeleton.AnimationTextureSize, 0, PixelFormat.Rgba, PixelType.Float, animationMatrices);
                GL.BindTexture(TextureTarget.Texture2D, 0);
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

        public void SetSkin(string skin)
        {
            var materialGroups = Model.Data.GetArray<IKeyValueCollection>("m_materialGroups");
            string[] defaultMaterials = null;

            foreach (var materialGroup in materialGroups)
            {
                // "The first item needs to match the default materials on the model"
                defaultMaterials ??= materialGroup.GetArray<string>("m_materials");

                if (materialGroup.GetProperty<string>("m_name") == skin)
                {
                    var materials = materialGroup.GetArray<string>("m_materials");

                    skinMaterials = new Dictionary<string, string>();

                    for (var i = 0; i < defaultMaterials.Length; i++)
                    {
                        skinMaterials[defaultMaterials[i]] = materials[i];
                    }

                    break;
                }
            }

            if (meshRenderers.Count > 0)
            {
                foreach (var mesh in meshRenderers)
                {
                    mesh.SetSkin(skinMaterials);
                }
            }
        }

        public void LoadAnimations()
        {
            if (loadedAnimations)
            {
                return;
            }
            loadedAnimations = true;
            animations.AddRange(Model.GetAllAnimations(Scene.GuiContext.FileLoader));

            if (animations.Any())
            {
                LoadSkeletons();
                SetupAnimationTextures();
            }
        }

        private void LoadMeshes()
        {
            // Get embedded meshes
            foreach (var embeddedMesh in Model.GetEmbeddedMeshesAndLoD().Where(m => (m.LoDMask & 1) != 0))
            {
                meshRenderers.Add(new RenderableMesh(embeddedMesh.Mesh, Scene.GuiContext, skinMaterials));
            }

            // Load referred meshes from file (only load meshes with LoD 1)
            var referredMeshesAndLoDs = Model.GetReferenceMeshNamesAndLoD();
            foreach (var refMesh in referredMeshesAndLoDs.Where(m => (m.LoDMask & 1) != 0))
            {
                var newResource = Scene.GuiContext.LoadFileByAnyMeansNecessary(refMesh.MeshName + "_c");
                if (newResource == null)
                {
                    continue;
                }

                meshRenderers.Add(new RenderableMesh(new Mesh(newResource), Scene.GuiContext, skinMaterials));
            }

            // Set active meshes to default
            SetActiveMeshGroups(Model.GetDefaultMeshGroups());
        }

        private void LoadSkeletons()
        {
            skeletons = meshRenderers.Select((_, i) => Model.GetSkeleton(i)).ToArray();
        }

        private void SetupAnimationTextures()
        {
            if (animationTextures == default)
            {
                // Create animation texture for each mesh
                animationTextures = new int[meshRenderers.Count];
                for (var i = 0; i < meshRenderers.Count; i++)
                {
                    var animationTexture = GL.GenTexture();
                    GL.BindTexture(TextureTarget.Texture2D, animationTexture);
                    // Set clamping to edges
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                    // Set nearest-neighbor sampling since we don't want to interpolate matrix rows
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
                    //Unbind texture again
                    GL.BindTexture(TextureTarget.Texture2D, 0);

                    animationTextures[i] = animationTexture;
                }
            }
        }

        public IEnumerable<string> GetSupportedAnimationNames()
            => animations.Select(a => a.Name);

        public void SetAnimation(string animationName)
        {
            activeAnimation = animations.FirstOrDefault(a => a.Name == animationName);
            animationFrameCache.Clear();
            AnimationController.SetAnimation(activeAnimation);

            if (activeAnimation != default)
            {
                for (var i = 0; i < meshRenderers.Count; i++)
                {
                    meshRenderers[i].SetAnimationTexture(animationTextures[i], skeletons[i].AnimationTextureSize);
                }
            }
            else
            {
                foreach (var renderer in meshRenderers)
                {
                    renderer.SetAnimationTexture(null, 0);
                }
            }
        }

        public IEnumerable<string> GetMeshGroups()
            => Model.GetMeshGroups();

        public ICollection<string> GetActiveMeshGroups()
            => activeMeshGroups;

        public void SetActiveMeshGroups(IEnumerable<string> meshGroups)
        {
            activeMeshGroups = new HashSet<string>(GetMeshGroups().Intersect(meshGroups));

            var groups = GetMeshGroups();
            if (groups.Count() > 1)
            {
                activeMeshRenderers.Clear();
                foreach (var group in activeMeshGroups)
                {
                    var meshMask = Model.GetActiveMeshMaskForGroup(group).ToArray();
                    for (var meshIndex = 0; meshIndex < meshRenderers.Count; meshIndex++)
                    {
                        if (meshMask[meshIndex] && !activeMeshRenderers.Contains(meshRenderers[meshIndex]))
                        {
                            activeMeshRenderers.Add(meshRenderers[meshIndex]);
                        }
                    }
                }
            }
            else
            {
                activeMeshRenderers = new HashSet<RenderableMesh>(meshRenderers);
            }
        }

        private void UpdateBoundingBox()
        {
            var first = true;
            foreach (var mesh in meshRenderers)
            {
                LocalBoundingBox = first ? mesh.BoundingBox : BoundingBox.Union(mesh.BoundingBox);
                first = false;
            }
        }
    }
}
