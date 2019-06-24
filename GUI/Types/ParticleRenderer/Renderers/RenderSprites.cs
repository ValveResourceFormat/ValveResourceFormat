using System;
using System.Collections.Generic;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
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

            uniform vec2 uUvOffset;
            uniform vec2 uUvScale;

            uniform float uAlpha;
            uniform float uOverbrightFactor;

            out vec4 fragColor;

            void main(void) {
                vec4 color = texture(uTexture, uUvOffset + uv * uUvScale);
                vec3 finalColor = uColor * uAlpha * color.w * color.xyz;
                float luminance = 0.2126 * finalColor.x + 0.7152 * finalColor.y + 0.0722 * finalColor.z;

                // TODO: Figure out what the proper function is
                //fragColor = vec4(uColor * uAlpha * color.w * color.xyz, luminance); 
                fragColor = vec4(uColor * uAlpha * color.w * color.xyz, uOverbrightFactor * (finalColor.x + finalColor.y + finalColor.z) / 3.0);
            }";

        private readonly int shaderProgram;
        private readonly int quadVao;
        private readonly int glTexture;

        private readonly Texture.SpritesheetData spriteSheetData;
        private readonly float animationRate = 0.1f;

        private readonly bool additive;
        private readonly float overbrightFactor = 1;
        private readonly long orientationType = 0;

        public RenderSprites(IKeyValueCollection keyValues, VrfGuiContext vrfGuiContext)
        {
            shaderProgram = SetupShaderProgram();

            // The same quad is reused for all particles
            quadVao = SetupQuadBuffer();

            var textureSetup = LoadTexture(keyValues.GetProperty<string>("m_hTexture"), vrfGuiContext);
            glTexture = textureSetup.TextureIndex;
            spriteSheetData = textureSetup.TextureData.GetSpriteSheetData();

            additive = keyValues.GetProperty<bool>("m_bAdditive");
            if (keyValues.ContainsKey("m_flOverbrightFactor"))
            {
                overbrightFactor = keyValues.GetFloatProperty("m_flOverbrightFactor");
            }

            if (keyValues.ContainsKey("m_nOrientationType"))
            {
                orientationType = keyValues.GetIntegerProperty("m_nOrientationType");
            }

            if (keyValues.ContainsKey("m_flAnimationRate"))
            {
                animationRate = keyValues.GetFloatProperty("m_flAnimationRate");
            }
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

        private (int TextureIndex, Texture TextureData) LoadTexture(string textureName, VrfGuiContext vrfGuiContext)
        {
            var materialLoader = new MaterialLoader(vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);

            var textureResource = FileExtensions.LoadFileByAnyMeansNecessary(textureName + "_c", vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);

            return (materialLoader.LoadTexture(textureName), (Texture)textureResource.Blocks[BlockType.DATA]);
        }

        public void Render(IEnumerable<Particle> particles, Matrix4 projectionMatrix, Matrix4 modelViewMatrix)
        {
            GL.UseProgram(shaderProgram);

            if (additive)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, glTexture);

            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uTexture"), 0); // set texture unit 0 as uTexture uniform
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uProjectionMatrix"), false, ref projectionMatrix);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uModelViewMatrix"), false, ref modelViewMatrix);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uOverbrightFactor"), overbrightFactor);

            var modelMatrixLocation = GL.GetUniformLocation(shaderProgram, "uModelMatrix");
            var colorLocation = GL.GetUniformLocation(shaderProgram, "uColor");
            var alphaLocation = GL.GetUniformLocation(shaderProgram, "uAlpha");
            var uvOffsetLocation = GL.GetUniformLocation(shaderProgram, "uUvOffset");
            var uvScaleLocation = GL.GetUniformLocation(shaderProgram, "uUvScale");

            var modelViewRotation = modelViewMatrix.ExtractRotation().Inverted(); // Create billboarding rotation (always facing camera)
            var billboardMatrix = Matrix4.CreateFromQuaternion(modelViewRotation);

            foreach (var particle in particles)
            {
                var modelMatrix = orientationType == 0
                    ? particle.GetRotationMatrix() * billboardMatrix * particle.GetTransformationMatrix()
                    : particle.GetRotationMatrix() * particle.GetTransformationMatrix();

                // Position/Radius uniform
                GL.UniformMatrix4(modelMatrixLocation, false, ref modelMatrix);

                if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                {
                    var sequence = spriteSheetData.Sequences[0];

                    var particleTime = particle.ConstantLifetime - particle.Lifetime;
                    var frame = particleTime * sequence.FramesPerSecond * animationRate;

                    var currentFrame = sequence.Frames[(int)Math.Floor(frame) % sequence.Frames.Length];

                    // Lerp frame coords and size
                    var subFrameTime = frame % 1.0f;
                    var offset = (currentFrame.StartMins * (1 - subFrameTime)) + (currentFrame.EndMins * subFrameTime);
                    var scale = ((currentFrame.StartMaxs - currentFrame.StartMins) * (1 - subFrameTime))
                            + ((currentFrame.EndMaxs - currentFrame.EndMins) * subFrameTime);

                    GL.Uniform2(uvOffsetLocation, offset.X, offset.Y);
                    GL.Uniform2(uvScaleLocation, scale.X, scale.Y);
                }
                else
                {
                    GL.Uniform2(uvOffsetLocation, 1f, 1f);
                    GL.Uniform2(uvScaleLocation, 1f, 1f);
                }

                // Color uniform
                GL.Uniform3(colorLocation, particle.Color.X, particle.Color.Y, particle.Color.Z);

                GL.Uniform1(alphaLocation, particle.Alpha);

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            GL.BindVertexArray(0);
            GL.UseProgram(0);

            if (additive)
            {
                GL.BlendEquation(BlendEquationMode.FuncAdd);
            }
        }
    }
}
