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

            // todo: bounding box should be from current frame vertices
            var sizeCap = LocalBoundingBox.Size.Length();
            if (sizeCap < 1f)
            {
                sizeCap = 100f;
            }

            var distance = Vector3.Distance(camera.Location, Vector3.Transform(boneMatrix.Translation, Transform));
            var distanceFade = distance > sizeCap ? sizeCap / distance : 1f;

            textRenderer.AddTextBillboard(Vector3.Transform(boneMatrix.Translation, Transform), new TextRenderer.TextRenderRequest
            {
                Scale = 10f * distanceFade,
                Text = bone.Name,
                Color = (bone.Parent, bone.Children.Count) switch
                {
                    (null, _) => new Color32(1.0f, 0.8f, 0.8f, 1.0f),
                    (_, 0) => new Color32(0.8f, 1.0f, 0.8f, 1.0f),
                    _ => Color32.White,
                },
            }, camera);

            if (bone.Parent != null)
            {
                var parentMatrix = animation.Pose[bone.Parent.Index];

                ShapeSceneNode.AddLine(vertices, boneMatrix.Translation, parentMatrix.Translation, Color32.White);
            }

            // Bone space axes, normalized to strip any scale from the pose.
            // Sized proportionally to camera distance so they stay constant on screen.
            var origin = boneMatrix.Translation;
            var axisLength = 0.04f * MathF.Min(distance, sizeCap);

            ShapeSceneNode.AddLine(vertices, origin, origin + Vector3.Normalize(new Vector3(boneMatrix.M11, boneMatrix.M12, boneMatrix.M13)) * axisLength, new(1.0f, 0.2f, 0.2f, 1.0f));
            ShapeSceneNode.AddLine(vertices, origin, origin + Vector3.Normalize(new Vector3(boneMatrix.M21, boneMatrix.M22, boneMatrix.M23)) * axisLength, new(0.2f, 0.8f, 0.2f, 1.0f));
            ShapeSceneNode.AddLine(vertices, origin, origin + Vector3.Normalize(new Vector3(boneMatrix.M31, boneMatrix.M32, boneMatrix.M33)) * axisLength, new(0.2f, 0.2f, 1.0f, 1.0f));

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

            lineBuffer.Draw(Id, context.ReplacementShader);

            GL.DepthFunc(DepthFunction.Greater);
        }
    }
}
