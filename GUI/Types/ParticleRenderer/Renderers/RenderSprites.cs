using System;
using System.Collections.Generic;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Renderers
{
    internal class RenderSprites : IParticleRenderer
    {
        private readonly Shader shader;
        private readonly int quadVao;
        private readonly int glTexture;

        private readonly Texture.SpritesheetData spriteSheetData;
        private readonly float animationRate = 0.1f;

        private readonly bool additive;
        private readonly float overbrightFactor = 1;
        private readonly long orientationType = 0;

        public RenderSprites(IKeyValueCollection keyValues, VrfGuiContext vrfGuiContext)
        {
            shader = vrfGuiContext.ShaderLoader.LoadShader("vrf.particle.sprite", new Dictionary<string, bool>());

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
        }

        private int SetupQuadBuffer()
        {
            GL.UseProgram(shader.Program);

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

            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.BindVertexArray(0); // Unbind VAO

            return vao;
        }

        private (int TextureIndex, Texture TextureData) LoadTexture(string textureName, VrfGuiContext vrfGuiContext)
        {
            var textureResource = vrfGuiContext.LoadFileByAnyMeansNecessary(textureName + "_c");

            if (textureResource == null)
            {
                return (vrfGuiContext.MaterialLoader.GetErrorTexture(), null);
            }

            return (vrfGuiContext.MaterialLoader.LoadTexture(textureName), (Texture)textureResource.DataBlock);
        }

        public void Render(IEnumerable<Particle> particles, Matrix4 projectionMatrix, Matrix4 modelViewMatrix)
        {
            GL.Enable(EnableCap.Blend);
            GL.UseProgram(shader.Program);

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

            GL.Uniform1(shader.GetUniformLocation("uTexture"), 0); // set texture unit 0 as uTexture uniform
            GL.UniformMatrix4(shader.GetUniformLocation("uProjectionMatrix"), false, ref projectionMatrix);
            GL.UniformMatrix4(shader.GetUniformLocation("uModelViewMatrix"), false, ref modelViewMatrix);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            GL.Uniform1(shader.GetUniformLocation("uOverbrightFactor"), overbrightFactor);

            var modelMatrixLocation = shader.GetUniformLocation("uModelMatrix");
            var colorLocation = shader.GetUniformLocation("uColor");
            var alphaLocation = shader.GetUniformLocation("uAlpha");
            var uvOffsetLocation = shader.GetUniformLocation("uUvOffset");
            var uvScaleLocation = shader.GetUniformLocation("uUvScale");

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
                    var sequence = spriteSheetData.Sequences[particle.Sequence % spriteSheetData.Sequences.Length];

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
