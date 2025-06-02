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
        private readonly TextRenderer textRenderer;
        readonly Shader shader;
        readonly int vaoHandle;
        readonly int vboHandle;
        int vertexCount;

        public SkeletonSceneNode(Scene scene, AnimationController animationController, Skeleton skeleton, TextRenderer textRenderer)
            : base(scene)
        {
            this.animationController = animationController;
            this.skeleton = skeleton;
            this.textRenderer = textRenderer;

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

        public List<(Vector3 Position, string String)> TextCalls { get; private set; } = [];
        public override void Update(Scene.UpdateContext context)
        {
            if (!Enabled)
            {
                return;
            }

            LocalBoundingBox = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

            TextCalls.Clear();
            Frame frame = null;
            if (animationController.ActiveAnimation != null)
            {
                if (animationController.IsPaused)
                {
                    frame = animationController.FrameCache.GetFrame(animationController.ActiveAnimation, animationController.Frame);
                }
                else
                {
                    frame = animationController.FrameCache.GetInterpolatedFrame(animationController.ActiveAnimation, animationController.Time);
                }
            }

            var vertices = new List<SimpleVertex>();

            foreach (var root in skeleton.Roots)
            {
                GetAnimationMatrixRecursive(vertices, TextCalls, root, Matrix4x4.Identity, frame);
            }

            vertexCount = vertices.Count;

            GL.NamedBufferData(vboHandle, vertices.Count * SimpleVertex.SizeInBytes, ListAccessors<SimpleVertex>.GetBackingArray(vertices), BufferUsageHint.DynamicDraw);
        }

        private static void GetAnimationMatrixRecursive(List<SimpleVertex> vertices, List<(Vector3 Position, string String)> text, Bone bone, Matrix4x4 bindPose, Frame frame)
        {
            var oldBindPose = bindPose;

            if (frame != null)
            {
                var transform = frame.Bones[bone.Index];
                bindPose = Matrix4x4.CreateScale(transform.Scale)
                    * Matrix4x4.CreateFromQuaternion(transform.Angle)
                    * Matrix4x4.CreateTranslation(transform.Position)
                    * bindPose;
            }
            else
            {
                bindPose = bone.BindPose * bindPose;
            }

            text.Add((bindPose.Translation, bone.Name));

            if (!oldBindPose.IsIdentity)
            {
                var fade = Random.Shared.NextSingle() * 0.5f + 0.5f;
                OctreeDebugRenderer<SceneNode>.AddLine(vertices, bindPose.Translation, oldBindPose.Translation, new(1f - fade, 1f, fade, 1f));
            }

            foreach (var child in bone.Children)
            {
                GetAnimationMatrixRecursive(vertices, text, child, bindPose, frame);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (!Enabled)
            {
                return;
            }

            if (context.RenderPass == RenderPass.Translucent)
            {
                foreach (var text in TextCalls)
                {
                    textRenderer.RenderTextBillboard(context.View.Camera, text.Position, 3f, Vector4.One, text.String, center: false);
                }

                return;
            }

            if (context.RenderPass != RenderPass.AfterOpaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            GL.DepthFunc(DepthFunction.Always);

            renderShader.Use();
            renderShader.SetUniform4x4("transform", Transform);
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
