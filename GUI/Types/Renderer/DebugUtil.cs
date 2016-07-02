using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Types.Renderer
{
    class DebugUtil
    {
        private const string vShaderSource = @"
#version 330
attribute vec3 aVertexPos;

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
        private List<DebugObject> objects;
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

            return;
        }

        public void AddCube(Vector3 position, float scale)
        {
            AddCube(Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(position));
        }

        public void AddCube(Matrix4 transform)
        {
            // Create new buffer
            int buffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            float[] vertices = {
                -1f, -1f, -1f, 1f, -1f, -1f, -1f, -1f, 1f, -1f, -1f, 1f, 1f, -1f, -1f, 1f, -1f, 1f, //Front face
                1f, 1f, -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f, 1f, -1f, -1f, 1f, 1f, 1f, 1f, 1f, //Back face
                1f, -1f, -1f, 1f, 1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f, 1f, 1f, -1f, 1f, 1f, 1f, //Right face
                -1f, 1f, -1f, -1f, -1f, -1f, -1f, -1f, 1f, -1f, 1f, -1f, -1f, -1f, 1f, -1f, 1f, 1f, //Left face
                -1f, -1f, 1f, 1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f, 1f, 1f, -1f, 1f, 1f, 1f, 1f, //Top face
                1f, -1f, -1f, -1f, -1f, -1f, -1f, 1f, -1f, 1f, -1f, -1f, -1f, 1f, -1f, 1f, 1f, -1f //Bottom face
            };
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(sizeof(float) * vertices.Length), vertices, BufferUsageHint.StaticDraw);

            objects.Add(new DebugObject(buffer, vertices.Length/3, transform));
        }

        public void Draw(Camera camera)
        {
            // Bind debug shader
            GL.UseProgram(shaderProgram);
            var posAttribute = GL.GetAttribLocation(shaderProgram, "aVertexPos");
            GL.EnableVertexAttribArray(posAttribute);

            // Bind camera matrix uniforms
            var uniformLocation = GL.GetUniformLocation(shaderProgram, "uProjection");
            GL.UniformMatrix4(uniformLocation, false, ref camera.ProjectionMatrix);
            uniformLocation = GL.GetUniformLocation(shaderProgram, "uView");
            GL.UniformMatrix4(uniformLocation, false, ref camera.CameraViewMatrix);

            foreach (var obj in objects) {
                GL.EnableVertexAttribArray(0);

                uniformLocation = GL.GetUniformLocation(shaderProgram, "uTransform");
                var transform = obj.Transform;
                GL.UniformMatrix4(uniformLocation, false, ref transform);

                GL.BindBuffer(BufferTarget.ArrayBuffer, obj.Buffer);
                GL.VertexAttribPointer(posAttribute, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, 0);

                GL.DrawArrays(PrimitiveType.Triangles, 0, obj.Size);
            }

            // Unbind debug shader
            GL.UseProgram(0);
        }

        private struct DebugObject
        {
            public int Buffer;
            public int Size;
            public Matrix4 Transform;

            public DebugObject(int buffer, int size, Matrix4 transform)
            {
                Buffer = buffer;
                Size = size;
                Transform = transform;
            }
        }
    }
}
