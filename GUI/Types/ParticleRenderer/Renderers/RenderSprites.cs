using System;
using System.Collections.Generic;
using GUI.Types.Renderer;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Renderers
{
    internal class RenderSprites : IParticleRenderer
    {
        private const string VertextShaderSource = @"
            #version 400
            in vec3 aVertexPosition;

            uniform mat4 uProjectionMatrix;
            uniform mat4 uModelViewMatrix;

            uniform mat4 uModelMatrix;

            out vec2 uv;

            void main(void) {
                uv = aVertexPosition.xy * 0.5 + 0.5;
                gl_Position = uProjectionMatrix * uModelViewMatrix * uModelMatrix * vec4(aVertexPosition, 1.0);
            }";

        private const string FragmentShaderSource = @"
            #version 400

            uniform vec3 uColor;
            uniform sampler2D uTexture;

            in vec2 uv;

            out vec4 fragColor;

            void main(void) {
                fragColor = texture(uTexture, uv);
            }";

        private readonly int shaderProgram;
        private readonly int quadVao;
        private readonly int texture;

        private readonly bool additive;

        public RenderSprites(IKeyValueCollection keyValues, VrfGuiContext vrfGuiContext)
        {
            shaderProgram = SetupShaderProgram();

            // The same quad is reused for all particles
            quadVao = SetupQuadBuffer();

            texture = LoadTexture(keyValues.GetProperty<string>("m_hTexture"), vrfGuiContext);

            additive = keyValues.GetProperty<bool>("m_bAdditive");
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
            GL.UseProgram(shaderProgram);

            // Create and bind VAO
            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            var vertices = new float[]
            {
                -1.0f, -1.0f, 0.0f,
                -1.0f, 1.0f, 0.0f,
                1.0f, -1.0f, 0.0f,
                1.0f, 1.0f, 0.0f,
            };

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);

            var positionAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexPosition");
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.BindVertexArray(0); // Unbind VAO

            return vao;
        }

        private int LoadTexture(string textureName, VrfGuiContext vrfGuiContext)
        {
            var materialLoader = new MaterialLoader(vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);
            return materialLoader.LoadTexture(textureName);
            //var texture = GL.GenTexture();

            //GL.BindTexture(TextureTarget.Texture2D, texture);

            //var data = new byte[]
            //{
            //    255, 0, 0, 255,
            //    0, 255, 0, 255,
            //};

            //GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 2, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);

            //GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            //GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            //return texture;
        }

        public void Render(IEnumerable<Particle> particles, Matrix4 projectionMatrix, Matrix4 modelViewMatrix)
        {
            GL.UseProgram(shaderProgram);

            if (additive)
            {
                GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            }
            else
            {
                GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.One);
            }

            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture);

            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uTexture"), 0); // set texture unit 0 as uTexture uniform
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uProjectionMatrix"), false, ref projectionMatrix);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uModelViewMatrix"), false, ref modelViewMatrix);

            var modelMatrixLocation = GL.GetUniformLocation(shaderProgram, "uModelMatrix");
            var colorLocation = GL.GetUniformLocation(shaderProgram, "uColor");

            var modelViewRotation = modelViewMatrix.ExtractRotation().Inverted(); // Create billboarding rotation (always facing camera)
            var rotationMatrix = Matrix4.CreateFromQuaternion(modelViewRotation);

            foreach (var particle in particles)
            {
                var scaleMatrix = Matrix4.CreateScale(particle.Radius);
                var translationMatrix = Matrix4.CreateTranslation(particle.Position.X, particle.Position.Y, particle.Position.Z);

                var modelMatrix = scaleMatrix * rotationMatrix * translationMatrix;

                // Position/Radius uniform
                GL.UniformMatrix4(modelMatrixLocation, false, ref modelMatrix);

                // Color uniform
                GL.Uniform3(colorLocation, particle.Color.X, particle.Color.Y, particle.Color.Z);

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
    }
}
