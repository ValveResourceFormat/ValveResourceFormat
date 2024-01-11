using System.Collections.Generic;
using System.Numerics;
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

            const int stride = sizeof(float) * 7;
            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);

            var colorAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexColor");
            GL.EnableVertexAttribArray(colorAttributeLocation);
            GL.VertexAttribPointer(colorAttributeLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);

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

            var vertices = new List<float>();

            foreach (var root in skeleton.Roots)
            {
                GetAnimationMatrixRecursive(vertices, root, Matrix4x4.Identity, frame);
            }

            vertexCount = vertices.Count / 7;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.DynamicDraw);
        }

        private static void GetAnimationMatrixRecursive(List<float> vertices, Bone bone, Matrix4x4 bindPose, Frame frame)
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
                OctreeDebugRenderer<SceneNode>.AddLine(vertices, bindPose.Translation, oldBindPose.Translation, 0, 1, 1, 1);
            }

            foreach (var child in bone.Children)
            {
                GetAnimationMatrixRecursive(vertices, child, bindPose, frame);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (!Enabled)
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
