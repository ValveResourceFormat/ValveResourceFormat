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
    internal class RenderTrails : IParticleRenderer
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

                vec3 finalColor = uColor * color.xyz;
                float blendingFactor = uOverbrightFactor * (finalColor.x + finalColor.y + finalColor.z) / 3.0;

                fragColor = vec4(finalColor, uAlpha * color.w * blendingFactor);
            }";

        private readonly int shaderProgram;
        private readonly int quadVao;
        private readonly int glTexture;

        private readonly Texture.SpritesheetData spriteSheetData;
        private readonly float animationRate = 0.1f;

        private readonly bool additive;
        private readonly float overbrightFactor = 1;
        private readonly long orientationType = 0;

        private readonly float finalTextureScaleU = 1f;
        private readonly float finalTextureScaleV = 1f;

        private readonly float maxLength = 2000f;
        private readonly float lengthFadeInTime = 0f;

        public RenderTrails(IKeyValueCollection keyValues, VrfGuiContext vrfGuiContext)
        {
            shaderProgram = SetupShaderProgram();

            // The same quad is reused for all particles
            quadVao = SetupQuadBuffer();

            if (keyValues.ContainsKey("m_hTexture"))
            {
                var textureSetup = LoadTexture(keyValues.GetProperty<string>("m_hTexture"), vrfGuiContext);
                glTexture = textureSetup.TextureIndex;
                spriteSheetData = textureSetup.TextureData.GetSpriteSheetData();
            }
            else
            {
                glTexture = vrfGuiContext.MaterialLoader.GetErrorTexture();
            }

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

            if (keyValues.ContainsKey("m_flFinalTextureScaleU"))
            {
                finalTextureScaleU = keyValues.GetFloatProperty("m_flFinalTextureScaleU");
            }

            if (keyValues.ContainsKey("m_flFinalTextureScaleV"))
            {
                finalTextureScaleV = keyValues.GetFloatProperty("m_flFinalTextureScaleV");
            }

            if (keyValues.ContainsKey("m_flMaxLength"))
            {
                maxLength = keyValues.GetFloatProperty("m_flMaxLength");
            }

            if (keyValues.ContainsKey("m_flLengthFadeInTime"))
            {
                lengthFadeInTime = keyValues.GetFloatProperty("m_flLengthFadeInTime");
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
            var textureResource = vrfGuiContext.LoadFileByAnyMeansNecessary(textureName + "_c");

            return (vrfGuiContext.MaterialLoader.LoadTexture(textureName), (Texture)textureResource.DataBlock);
        }

        public void Render(IEnumerable<Particle> particles, Matrix4 projectionMatrix, Matrix4 modelViewMatrix)
        {
            GL.Enable(EnableCap.Blend);
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
                var position = new Vector3(particle.Position.X, particle.Position.Y, particle.Position.Z);
                var previousPosition = new Vector3(particle.PositionPrevious.X, particle.PositionPrevious.Y, particle.PositionPrevious.Z);
                var difference = previousPosition - position;
                var direction = difference.Normalized();

                var midPoint = position + (0.5f * difference);

                // Trail width = radius
                // Trail length = distance between current and previous times trail length divided by 2 (because the base particle is 2 wide)
                var length = Math.Min(maxLength, particle.TrailLength * difference.Length / 2f);
                var t = 1 - (particle.Lifetime / particle.ConstantLifetime);
                var animatedLength = t >= lengthFadeInTime
                    ? length
                    : t * length / lengthFadeInTime;
                var scaleMatrix = Matrix4.CreateScale(particle.Radius, animatedLength, 1);

                // Center the particle at the midpoint between the two points
                var translationMatrix = Matrix4.CreateTranslation(Vector3.UnitY * animatedLength);

                // Calculate rotation matrix

                var axis = Vector3.Cross(Vector3.UnitY, direction).Normalized();
                var angle = (float)Math.Acos(direction.Y);
                var rotationMatrix = Matrix4.CreateFromAxisAngle(axis, angle);

                var modelMatrix = orientationType == 0
                    ? scaleMatrix * translationMatrix * rotationMatrix
                    : particle.GetTransformationMatrix();

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
                    GL.Uniform2(uvScaleLocation, scale.X * finalTextureScaleU, scale.Y * finalTextureScaleV);
                }
                else
                {
                    GL.Uniform2(uvOffsetLocation, 1f, 1f);
                    GL.Uniform2(uvScaleLocation, finalTextureScaleU, finalTextureScaleV);
                }

                // Color uniform
                GL.Uniform3(colorLocation, particle.Color.X, particle.Color.Y, particle.Color.Z);

                GL.Uniform1(alphaLocation, particle.Alpha * particle.AlphaAlternate);

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            GL.BindVertexArray(0);
            GL.UseProgram(0);

            if (additive)
            {
                GL.BlendEquation(BlendEquationMode.FuncAdd);
            }

            GL.Disable(EnableCap.Blend);
        }
    }
}
