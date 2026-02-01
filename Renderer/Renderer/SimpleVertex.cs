using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Simple vertex with position and color for debug rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public record struct SimpleVertex(Vector3 Position, Color32 Color)
    {
        public static readonly int SizeInBytes = Marshal.SizeOf<SimpleVertex>();

        public static void BindDefaultShaderLayout(int vao, int shaderProgram)
        {
            var positionAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexPosition");
            var colorAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexColor");

            GL.EnableVertexArrayAttrib(vao, positionAttributeLocation);
            GL.EnableVertexArrayAttrib(vao, colorAttributeLocation);

            GL.VertexArrayAttribFormat(vao, positionAttributeLocation, 3, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribFormat(vao, colorAttributeLocation, 4, VertexAttribType.UnsignedByte, true,
                sizeof(float) * 3);

            GL.VertexArrayAttribBinding(vao, positionAttributeLocation, 0);
            GL.VertexArrayAttribBinding(vao, colorAttributeLocation, 0);
        }
    }

    /// <summary>
    /// Simple vertex with position, color, and normal for debug rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public record struct SimpleVertexNormal(Vector3 Position, Color32 Color, Vector3 Normal)
    {
        public static readonly int SizeInBytes = Marshal.SizeOf<SimpleVertexNormal>();

        public SimpleVertexNormal(Vector3 Position, Color32 Color) : this(Position, Color, Vector3.Zero) { }

        public static void BindDefaultShaderLayout(int vao, int shaderProgram)
        {
            SimpleVertex.BindDefaultShaderLayout(vao, shaderProgram);

            var normalAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexNormal");
            GL.EnableVertexArrayAttrib(vao, normalAttributeLocation);
            GL.VertexArrayAttribFormat(vao, normalAttributeLocation, 3, VertexAttribType.Float, false, sizeof(float) * 3 + sizeof(byte) * 4);
            GL.VertexArrayAttribBinding(vao, normalAttributeLocation, 0);
        }
    }
}
