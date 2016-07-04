using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class DebugUtil
    {
        private const string vShaderSource = @"
#version 330
in vec3 aVertexPos;

uniform mat4 uProjection;
uniform mat4 uView;

uniform mat4 uTransform;

void main(void)
{
    gl_Position = uProjection * uView * uTransform * vec4(aVertexPos, 1.0);
}";
        private const string fShaderSource = @"
#version 330
precision mediump float;

void main(void) {
    gl_FragColor = vec4(1,0,0,1);
}";
        private readonly List<DebugObject> objects;
        private int shaderProgram;

        public DebugUtil()
        {
            objects = new List<DebugObject>();
        }

        public void Setup()
        {
            // Set up shaders
            var vShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vShader, vShaderSource);
            GL.CompileShader(vShader);

            int shaderStatus;
            GL.GetShader(vShader, ShaderParameter.CompileStatus, out shaderStatus);
            if (shaderStatus != 1)
            {
                string vsInfo;
                GL.GetShaderInfoLog(vShader, out vsInfo);
                Console.WriteLine(vsInfo);
            }

            var fShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fShader, fShaderSource);
            GL.CompileShader(fShader);

            GL.GetShader(fShader, ShaderParameter.CompileStatus, out shaderStatus);
            if (shaderStatus != 1)
            {
                string vsInfo;
                GL.GetShaderInfoLog(vShader, out vsInfo);
                Console.WriteLine(vsInfo);
            }

            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vShader);
            GL.AttachShader(shaderProgram, fShader);
            GL.LinkProgram(shaderProgram);
            GL.ValidateProgram(shaderProgram);

            int linkStatus;
            GL.GetProgram(shaderProgram, GetProgramParameterName.LinkStatus, out linkStatus);
        }

        public void AddCube(Vector3 position, float scale)
        {
            AddCube(Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(position));
        }

        public void AddCube(Matrix4 transform)
        {
            // Create VAO
            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            // Create new buffer
            var buffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            float[] vertices =
            {
                -1f, -1f, -1f, 1f, -1f, -1f, -1f, -1f, 1f, -1f, -1f, 1f, 1f, -1f, -1f, 1f, -1f, 1f, //Front face
                1f, 1f, -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f, 1f, -1f, -1f, 1f, 1f, 1f, 1f, 1f, //Back face
                1f, -1f, -1f, 1f, 1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f, 1f, 1f, -1f, 1f, 1f, 1f, //Right face
                -1f, 1f, -1f, -1f, -1f, -1f, -1f, -1f, 1f, -1f, 1f, -1f, -1f, -1f, 1f, -1f, 1f, 1f, //Left face
                -1f, -1f, 1f, 1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f, 1f, 1f, -1f, 1f, 1f, 1f, 1f, //Top face
                1f, -1f, -1f, -1f, -1f, -1f, -1f, 1f, -1f, 1f, -1f, -1f, -1f, 1f, -1f, 1f, 1f, -1f //Bottom face
            };
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(sizeof(float) * vertices.Length), vertices, BufferUsageHint.StaticDraw);

            var posAttribute = GL.GetAttribLocation(shaderProgram, "aVertexPos");
            GL.EnableVertexAttribArray(posAttribute);
            GL.VertexAttribPointer(posAttribute, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, 0);

            GL.BindVertexArray(0);

            objects.Add(new DebugObject(vao, vertices.Length / 3, transform));
        }

        public void Draw(Camera camera, bool ztest)
        {
            //Disable culling
            GL.Disable(EnableCap.CullFace);
            // Disable z-testing if specified
            if (!ztest)
            {
                GL.Disable(EnableCap.DepthTest);
            }

            // Bind debug shader
            GL.UseProgram(shaderProgram);

            var uniformLocation = GL.GetUniformLocation(shaderProgram, "uProjection");
            GL.UniformMatrix4(uniformLocation, false, ref camera.ProjectionMatrix);
            uniformLocation = GL.GetUniformLocation(shaderProgram, "uView");
            GL.UniformMatrix4(uniformLocation, false, ref camera.CameraViewMatrix);

            foreach (var obj in objects)
            {
                uniformLocation = GL.GetUniformLocation(shaderProgram, "uTransform");
                var transform = obj.Transform;
                GL.UniformMatrix4(uniformLocation, false, ref transform);

                // Bind VAO to draw
                GL.BindVertexArray(obj.VAO);
                GL.DrawArrays(PrimitiveType.Triangles, 0, obj.Size);
                GL.BindVertexArray(0);
            }

            GL.UseProgram(0);

            // Re-enable depth testing
            if (!ztest)
            {
                GL.Enable(EnableCap.DepthTest);
            }

            // Re-enable culling
            GL.Enable(EnableCap.CullFace);
        }

        private struct DebugObject
        {
            public readonly int VAO;
            public readonly int Size;
            public readonly Matrix4 Transform;

            public DebugObject(int vao, int size, Matrix4 transform)
            {
                VAO = vao;
                Size = size;
                Transform = transform;
            }
        }
    }
}
