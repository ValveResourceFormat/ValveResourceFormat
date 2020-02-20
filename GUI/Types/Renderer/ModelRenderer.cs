using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace GUI.Types.Renderer
{
    internal class ModelRenderer : IMeshRenderer, IAnimationRenderer
    {
        public Model Model { get; }

        private readonly VrfGuiContext guiContext;

        private List<Animation> animations = new List<Animation>();
        private List<Material> materials = new List<Material>();

        private Animation activeAnimation;
        private int animationTexture;
        private Skeleton skeleton;

        private Stopwatch timer = new Stopwatch();
        private List<MeshRenderer> meshRenderers = new List<MeshRenderer>();

        public ModelRenderer(Model model, VrfGuiContext vrfGuiContext, bool loadAnimations = true)
        {
            Model = model;

            guiContext = vrfGuiContext;

            // Load required resources
            if (loadAnimations)
            {
                LoadSkeleton();
                LoadAnimations();
            }

            LoadMaterials();
            LoadMeshes();

            timer.Start();
        }

        public void Update(float frameTime)
        {
            if (activeAnimation == null)
            {
                return;
            }

            // Update animation matrices
            var animationMatrices = new float[skeleton.Bones.Length * 16];
            for (var i = 0; i < skeleton.Bones.Length; i++)
            {
                // Default to identity matrices
                animationMatrices[i * 16] = 1.0f;
                animationMatrices[(i * 16) + 5] = 1.0f;
                animationMatrices[(i * 16) + 10] = 1.0f;
                animationMatrices[(i * 16) + 15] = 1.0f;
            }

            animationMatrices = activeAnimation.GetAnimationMatricesAsArray((float)timer.Elapsed.TotalSeconds, skeleton);

            // Update animation texture
            GL.BindTexture(TextureTarget.Texture2D, animationTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 4, skeleton.Bones.Length, 0, PixelFormat.Rgba, PixelType.Float, animationMatrices);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void Render(Camera camera)
        {
            foreach (var meshRenderer in meshRenderers)
            {
                meshRenderer.Render(camera);
            }
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => meshRenderers.SelectMany(renderer => renderer.GetSupportedRenderModes()).Distinct();

        public void SetRenderMode(string renderMode)
        {
            foreach (var renderer in meshRenderers)
            {
                renderer.SetRenderMode(renderMode);
            }
        }

        public void SetMeshTransform(Matrix4 matrix)
        {
            foreach (var renderer in meshRenderers)
            {
                renderer.Transform = matrix;
            }
        }

        public void SetTint(Vector4 tint)
        {
            foreach (var renderer in meshRenderers)
            {
                renderer.Tint = tint;
            }
        }

        private void LoadMeshes()
        {
            // Get embedded meshes
            foreach (var embeddedMesh in Model.GetEmbeddedMeshes())
            {
                meshRenderers.Add(new MeshRenderer(embeddedMesh, guiContext));
            }

            // Load referred meshes from file (only load meshes with LoD 1)
            var referredMeshesAndLoDs = Model.GetReferenceMeshNamesAndLoD();
            foreach (var refMesh in referredMeshesAndLoDs.Where(m => (m.LoDMask & 1) != 0))
            {
                var newResource = guiContext.LoadFileByAnyMeansNecessary(refMesh.MeshName + "_c");
                if (newResource == null)
                {
                    continue;
                }

                if (!newResource.ContainsBlockType(BlockType.VBIB))
                {
                    Console.WriteLine("Old style model, no VBIB!");
                    continue;
                }

                meshRenderers.Add(new MeshRenderer(new Mesh(newResource), guiContext));
            }
        }

        private void LoadMaterials()
        {
            /*var materialGroups = Model.GetData().GetArray<IKeyValueCollection>("m_materialGroups");

            foreach (var materialGroup in materialGroups)
            {
                if (materialGroup.GetProperty<string>("m_name") == skin)
                {
                    var materials = materialGroup.GetArray<string>("m_materials");
                    skinMaterials.AddRange(materials);
                    break;
                }
            }*/
        }

        private void LoadSkeleton()
        {
            skeleton = Model.GetSkeleton();
        }

        private void LoadAnimations()
        {
            var animGroupPaths = Model.GetReferencedAnimationGroupNames();
            var emebeddedAnims = Model.GetEmbeddedAnimations();

            if (!animGroupPaths.Any() && !emebeddedAnims.Any())
            {
                return;
            }

            // Create animation texture
            animationTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, animationTexture);
            // Set clamping to edges
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            // Set nearest-neighbor sampling since we don't want to interpolate matrix rows
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            //Unbind texture again
            GL.BindTexture(TextureTarget.Texture2D, 0);

            // Load animations from referenced animation groups
            foreach (var animGroupPath in animGroupPaths)
            {
                var animGroup = guiContext.LoadFileByAnyMeansNecessary(animGroupPath + "_c");
                animations.AddRange(AnimationGroupLoader.LoadAnimationGroup(animGroup, guiContext));
            }

            // Get embedded animations
            animations.AddRange(emebeddedAnims);
        }

        public IEnumerable<string> GetSupportedAnimationNames()
            => animations.Select(a => a.Name);

        public void SetAnimation(string animationName)
        {
            activeAnimation = animations.FirstOrDefault(a => a.Name == animationName);
            if (activeAnimation != default)
            {
                foreach (var renderer in meshRenderers)
                {
                    renderer.SetAnimationTexture(animationTexture, skeleton.Bones.Length);
                }
            }
        }
    }
}
