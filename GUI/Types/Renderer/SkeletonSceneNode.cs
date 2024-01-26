using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

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
            GL.UseProgram(shader.Program);

            vaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(vaoHandle);

            vboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);

            SimpleVertex.BindDefaultShaderLayout(shader.Program);

            GL.BindVertexArray(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (!Enabled)
            {
                return;
            }

            LocalBoundingBox = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

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
                GetAnimationMatrixRecursive(vertices, root, Matrix4x4.Identity, frame);
            }

            vertexCount = vertices.Count;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * SimpleVertex.SizeInBytes, vertices.ToArray(), BufferUsageHint.DynamicDraw);
        }

        private static void GetAnimationMatrixRecursive(List<SimpleVertex> vertices, Bone bone, Matrix4x4 bindPose, Frame frame)
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

            if (!oldBindPose.IsIdentity)
            {
                OctreeDebugRenderer<SceneNode>.AddLine(vertices, bindPose.Translation, oldBindPose.Translation, new(0f, 1f, 1f, 1f));
            }

            foreach (var child in bone.Children)
            {
                GetAnimationMatrixRecursive(vertices, child, bindPose, frame);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (!Enabled || context.RenderPass != RenderPass.AfterOpaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            GL.UseProgram(renderShader.Program);

            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }
    }
}
