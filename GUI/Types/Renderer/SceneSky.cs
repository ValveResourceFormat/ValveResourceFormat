using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class SceneSky : SceneNode
    {
        public RenderMaterial Material { get; set; }
        private readonly int boxVao;
        private readonly float[] boxTriangles = {
            // positions          
            -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,
            1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,

            -1.0f, -1.0f,  1.0f,
            -1.0f, -1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f,  1.0f,
            -1.0f, -1.0f,  1.0f,

            1.0f, -1.0f, -1.0f,
            1.0f, -1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f,  1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,

            -1.0f, -1.0f,  1.0f,
            -1.0f,  1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f, -1.0f,  1.0f,
            -1.0f, -1.0f,  1.0f,

            -1.0f,  1.0f, -1.0f,
            1.0f,  1.0f, -1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            -1.0f,  1.0f,  1.0f,
            -1.0f,  1.0f, -1.0f,

            -1.0f, -1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f,
            1.0f, -1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f,
            1.0f, -1.0f,  1.0f
        };

        public SceneSky(Scene scene) : base(scene)
        {
            boxVao = GL.GenVertexArray();
            GL.BindVertexArray(boxVao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, boxTriangles.Length * sizeof(float), boxTriangles, BufferUsageHint.StaticDraw);

            //var positionAttributeLocation = GL.GetAttribLocation(Material.Shader.Program, "aVertexPosition");
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.BindVertexArray(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
        }

        public override void Render(Scene.RenderContext context)
        {
            GL.Disable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Front);
            GL.DepthFunc(DepthFunction.Lequal);

            GL.UseProgram(Material.Shader.Program);
            GL.BindVertexArray(boxVao);

            context.Camera.SetPerViewUniforms(Material.Shader);

            Material.Render();
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, boxTriangles.Length / 3);
            Material.PostRender();

            GL.BindVertexArray(0);
            GL.UseProgram(0);

            GL.DepthFunc(DepthFunction.Less);
            GL.CullFace(CullFaceMode.Back);
            GL.Enable(EnableCap.CullFace);
        }

        public override void SetRenderMode(string mode)
        {
            using var mat = Scene.GuiContext.LoadFileByAnyMeansNecessary(Scene.Sky?.Material.Material.Name + "_c");
            Material = Scene.GuiContext.MaterialLoader.LoadMaterial(mat);
        }
    }
}
