using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Renderers
{
    public class RenderSprites : IParticleRenderer
    {
        private const string VertextShaderSource = @"
            attribute vec3 aVertexPosition;

            uniform mat4 uProjectionMatrix;
            uniform mat4 uModelviewMatrix;

            uniform mat4 uModelMatrix;

            varying vec3 fragPos;

            void main(void) {
                fragPos = aVertexPosition;
                gl_Position = projectionMatrix * modelviewMatrix * modelMatrix * vec4(aVertexPosition, 1.0);
            }";

        private const string FragmentShaderSource = @"
            precision mediump float;

            uniform vec3 uColor;

            varying vec3 fragPos;

            void main(void) {
                gl_FragColor = vec4(color, 1.0);
            }";

        private readonly int shaderProgram;
        private readonly int quadBuffer;

        public RenderSprites(IKeyValueCollection keyValues)
        {
            shaderProgram = SetupShaderProgram();

            // The same quad is reused for all particles
            quadBuffer = SetupQuadBuffer();
        }

        private int SetupShaderProgram()
        {
            var shaderProgram = GL.CreateProgram();

            var vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, VertextShaderSource);
            GL.CompileShader(vertexShader);

            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out var vertextShaderStatus);
            if (vertextShaderStatus != 1)
            {
                GL.GetShaderInfoLog(vertexShader, out var vsInfo);
                throw new Exception($"Error setting up vertex shader : {vsInfo}");
            }

            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, FragmentShaderSource);
            GL.CompileShader(fragmentShader);

            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out var fragmentShaderStatus);
            if (fragmentShaderStatus != 1)
            {
                GL.GetShaderInfoLog(fragmentShader, out var fsInfo);
                throw new Exception($"Error setting up fragment shader : {fsInfo}");
            }

            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);

            GL.GetProgram(shaderProgram, GetProgramParameterName.LinkStatus, out var programStatus);
            if (programStatus != 1)
            {
                GL.GetProgramInfoLog(shaderProgram, out var programInfo);
                throw new Exception($"Error linking shader program: {programInfo}");
            }

            return shaderProgram;
        }

        private int SetupQuadBuffer()
        {
            var buffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);

            var vertices = new float[]
            {
                -1.0f, -1.0f, 0.0f,
                -1.0f, 1.0f, 0.0f,
                1.0f, -1.0f, 0.0f,
                1.0f, 1.0f, 0.0f,
            };

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, -1); // Unbind buffer

            return buffer;
        }

        public void Render(IEnumerable<Particle> particles, Matrix4 projectionMatrix, Matrix4 modelViewMatrix)
        {
            GL.UseProgram(shaderProgram);

            var positionAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexPosition");

            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uProjectionMatrix"), false, ref projectionMatrix);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uModelViewMatrix"), false, ref modelViewMatrix);

            GL.BindBuffer(BufferTarget.ArrayBuffer, quadBuffer);
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, 0, 0);

            var modelMatrixLocation = GL.GetUniformLocation(shaderProgram, "uModelMatrix");
            var colorLocation = GL.GetUniformLocation(shaderProgram, "uColor");

            var modelViewRotation = modelViewMatrix.ExtractRotation().Inverted(); // Create billboarding rotation (always facing camera)
            var rotationMatrix = Matrix4.CreateFromQuaternion(modelViewRotation);

            foreach (var particle in particles)
            {
                var scaleMatrix = Matrix4.CreateScale(particle.Radius);
                var translationMatrix = Matrix4.CreateTranslation(particle.Position.X, particle.Position.Y, particle.Position.Z);

                var modelMatrix = scaleMatrix * rotationMatrix * translationMatrix;

                //let modelMatrix = mat4.fromScaling(mat4.create(), vec3.fromValues(particle.radius, particle.radius, particle.radius));

                //let rotation = mat4.getRotation(quat.create(), modelViewMatrix);
                //quat.invert(rotation, rotation);

                //let rotationTransMatrix = mat4.fromRotationTranslation(mat4.create(), rotation, particle.position);
                //mat4.multiply(modelMatrix, rotationTransMatrix, modelMatrix);

                // Position/Radius uniform
                GL.UniformMatrix4(modelMatrixLocation, false, ref modelMatrix);

                // Color uniform
                GL.Uniform3(colorLocation, particle.Position.X, particle.Color.Y, particle.Color.Z);

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.UseProgram(-1);
        }
    }
}
