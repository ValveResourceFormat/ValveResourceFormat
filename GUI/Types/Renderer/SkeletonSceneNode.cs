using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

#nullable disable

namespace GUI.Types.Renderer
{
    class SkeletonSceneNode : SceneNode
    {
        public bool Enabled { get; set; }

        readonly AnimationController animationController;
        readonly Skeleton skeleton;
        readonly Shader shader;
        readonly int vaoHandle;
        readonly int vboHandle;
        int vertexCount;

        public SkeletonSceneNode(Scene scene, AnimationController animationController, Skeleton skeleton)
            : base(scene)
        {
            this.animationController = animationController;
            this.skeleton = skeleton;

            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.default");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

#if DEBUG
            var vaoLabel = nameof(SkeletonSceneNode);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (!Enabled)
            {
                return;
            }

            var vertices = new List<SimpleVertex>();

            foreach (var root in skeleton.Roots)
            {
                DrawSkeletonRecursive(root, vertices, context.View.TextRenderer, animationController);
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
            vertexCount = vertices.Count;

            GL.NamedBufferData(vboHandle, vertices.Count * SimpleVertex.SizeInBytes, ListAccessors<SimpleVertex>.GetBackingArray(vertices), BufferUsageHint.DynamicDraw);
        }

        private static void DrawSkeletonRecursive(Bone bone, List<SimpleVertex> vertices, TextRenderer textRenderer, AnimationController animation)
        {
            var boneMatrix = animation.Pose[bone.Index];

            textRenderer.AddTextBillboard(boneMatrix.Translation, new TextRenderer.TextRenderRequest
            {
                Scale = 10f,
                Text = bone.Name,
                Color = (bone.Parent, bone.Children.Count) switch
                {
                    (null, _) => new Color32(1.0f, 0.8f, 0.8f, 1.0f),
                    (_, 0) => new Color32(0.3f, 0.8f, 0.3f, 1.0f),
                    _ => Color32.White,
                },
                CenterVertical = false
            });

            if (bone.Parent != null)
            {
                var parentMatrix = animation.Pose[bone.Parent.Index];

                var fade = Random.Shared.NextSingle() * 0.5f + 0.5f;
                OctreeDebugRenderer<SceneNode>.AddLine(vertices, boneMatrix.Translation, parentMatrix.Translation, new(1f - fade, 1f, fade, 1f));
            }

            foreach (var child in bone.Children)
            {
                DrawSkeletonRecursive(child, vertices, textRenderer, animation);
            }
        }

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

            var renderShader = context.ReplacementShader ?? shader;

            GL.DepthFunc(DepthFunction.Always);

            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);
            renderShader.SetUniform1("sceneObjectId", Id);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.DepthFunc(DepthFunction.Greater);
        }
    }
}
