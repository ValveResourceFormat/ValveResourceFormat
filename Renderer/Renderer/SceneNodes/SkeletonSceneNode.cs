using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that visualizes skeletal bone hierarchy and animation poses.
    /// </summary>
    public class SkeletonSceneNode : SceneNode
    {
        /// <summary>Gets or sets whether the skeleton visualization is drawn.</summary>
        public bool Enabled { get; set; }

        readonly AnimationController animationController;
        readonly Skeleton skeleton;
        readonly LineBuffer lineBuffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SkeletonSceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="animationController">The animation controller providing bone pose data.</param>
        /// <param name="skeleton">The skeleton definition containing bone hierarchy.</param>
        public SkeletonSceneNode(Scene scene, AnimationController animationController, Skeleton skeleton)
            : base(scene)
        {
            this.animationController = animationController;
            this.skeleton = skeleton;

            lineBuffer = new LineBuffer(Scene.RendererContext, nameof(SkeletonSceneNode));
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!Enabled)
            {
                return;
            }

            var vertices = new List<SimpleVertex>();

            foreach (var root in skeleton.Roots)
            {
                DrawSkeletonRecursive(root, vertices, context.Camera, context.TextRenderer, animationController);
            }

            AABB bounds = default;
            var first = true;

            foreach (var vertex in vertices)
            {
                var vertexBounds = new AABB(vertex.Position, 10);

                if (first)
                {
                    bounds = vertexBounds;
                    first = false;
                    continue;
                }

                bounds = bounds.Union(vertexBounds);
            }

            LocalBoundingBox = bounds;

            lineBuffer.Upload(vertices);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            lineBuffer.Delete();
        }

        private void DrawSkeletonRecursive(Bone bone, List<SimpleVertex> vertices, Camera camera, TextRenderer textRenderer, AnimationController animation)
        {
            var boneMatrix = animation.Pose[bone.Index];

            textRenderer.AddTextBillboard(Vector3.Transform(boneMatrix.Translation, Transform), new TextRenderer.TextRenderRequest
            {
                Scale = 10f,
                Text = bone.Name,
                Color = (bone.Parent, bone.Children.Count) switch
                {
                    (null, _) => new Color32(1.0f, 0.8f, 0.8f, 1.0f),
                    (_, 0) => new Color32(0.3f, 0.8f, 0.3f, 1.0f),
                    _ => Color32.White,
                },
            }, camera);

            if (bone.Parent != null)
            {
                var parentMatrix = animation.Pose[bone.Parent.Index];

                var fade = Random.Shared.NextSingle() * 0.5f + 0.5f;
                ShapeSceneNode.AddLine(vertices, boneMatrix.Translation, parentMatrix.Translation, new(1f - fade, 1f, fade, 1f));
            }

            foreach (var child in bone.Children)
            {
                DrawSkeletonRecursive(child, vertices, camera, textRenderer, animation);
            }
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (!Enabled)
            {
                return;
            }

            if (context.RenderPass != RenderPass.Opaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? lineBuffer.Shader;

            GL.DepthFunc(DepthFunction.Always);

            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);

            lineBuffer.Draw(Id);

            GL.UseProgram(0);
            GL.DepthFunc(DepthFunction.Greater);
        }
    }
}
