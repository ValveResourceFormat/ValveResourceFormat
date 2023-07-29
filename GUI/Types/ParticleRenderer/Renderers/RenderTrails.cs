using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Renderers
{
    internal class RenderTrails : IParticleRenderer
    {
        private const string ShaderName = "vrf.particle.trail";

        private Shader shader;
        private readonly VrfGuiContext guiContext;
        private readonly int quadVao;
        private readonly RenderTexture texture;
        private readonly Texture.SpritesheetData spriteSheetData;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;

        private readonly bool additive;
        private readonly INumberProvider overbrightFactor = new LiteralNumberProvider(1);
        private readonly ParticleOrientation orientationType;
        private readonly ParticleField prevPositionSource = ParticleField.PositionPrevious; // this is a real thing

        private readonly float finalTextureScaleU = 1f;
        private readonly float finalTextureScaleV = 1f;

        private readonly float maxLength = 2000f;
        private readonly float lengthFadeInTime;

        private static bool wireframe;

        public RenderTrails(IKeyValueCollection keyValues, VrfGuiContext vrfGuiContext)
        {
            guiContext = vrfGuiContext;
            shader = vrfGuiContext.ShaderLoader.LoadShader(ShaderName);

            // The same quad is reused for all particles
            quadVao = SetupQuadBuffer();

            string textureName = null;

            if (keyValues.ContainsKey("m_hTexture"))
            {
                textureName = keyValues.GetProperty<string>("m_hTexture");
            }
            else if (keyValues.ContainsKey("m_vecTexturesInput"))
            {
                var textures = keyValues.GetArray("m_vecTexturesInput");

                if (textures.Length > 0)
                {
                    // TODO: Support more than one texture
                    textureName = textures[0].GetProperty<string>("m_hTexture");
                }
            }

            texture = vrfGuiContext.MaterialLoader.LoadTexture(textureName);
            spriteSheetData = texture.Data?.GetSpriteSheetData();

            additive = keyValues.GetProperty<bool>("m_bAdditive");
            if (keyValues.ContainsKey("m_flOverbrightFactor"))
            {
                overbrightFactor = keyValues.GetNumberProvider("m_flOverbrightFactor");
            }

            if (keyValues.ContainsKey("m_nOrientationType"))
            {
                orientationType = keyValues.GetEnumValue<ParticleOrientation>("m_nOrientationType", normalize: true);
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

            if (keyValues.ContainsKey("m_nAnimationType"))
            {
                animationType = keyValues.GetEnumValue<ParticleAnimationType>("m_nAnimationType");
            }

            if (keyValues.ContainsKey("m_nPrevPntSource"))
            {
                prevPositionSource = (ParticleField)keyValues.GetIntegerProperty("m_nPrevPntSource");
            }
        }

        public void SetWireframe(bool isWireframe)
        {
            wireframe = isWireframe;
            shader.SetUniform1("isWireframe", isWireframe ? 1 : 0);
        }

        private int SetupQuadBuffer()
        {
            GL.UseProgram(shader.Program);

            // Create and bind VAO
            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            var vertices = new[]
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

        public void Render(ParticleBag particleBag, ParticleSystemRenderState systemRenderState, Matrix4x4 viewProjectionMatrix, Matrix4x4 modelViewMatrix)
        {
            var particles = particleBag.LiveParticles;

            GL.Enable(EnableCap.Blend);


            if (additive)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            GL.UseProgram(shader.Program);

            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            // set texture unit 0 as uTexture uniform
            shader.SetTexture(0, "uTexture", texture);

            shader.SetUniform4x4("uProjectionViewMatrix", viewProjectionMatrix);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            // also todo: pass all of these as vertex parameters (probably just color/alpha combined)
            shader.SetUniform1("uOverbrightFactor", (float)overbrightFactor.NextNumber());

            // Create billboarding rotation (always facing camera)
            Matrix4x4.Decompose(modelViewMatrix, out _, out var modelViewRotation, out _);
            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            if (wireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            }

            // Todo: this could be adapted into renderropes without much difficulty
            foreach (ref var particle in particles)
            {
                var position = particle.Position;
                var previousPosition = particle.GetVector(prevPositionSource);
                var difference = previousPosition - position;
                var direction = Vector3.Normalize(difference);

                var midPoint = position + (0.5f * difference);

                // Trail width = radius
                // Trail length = distance between current and previous times trail length divided by 2 (because the base particle is 2 wide)
                var length = Math.Min(maxLength, particle.TrailLength * difference.Length() / 2f);
                var t = particle.NormalizedAge;
                var animatedLength = t >= lengthFadeInTime
                    ? length
                    : t * length / lengthFadeInTime;
                var scaleMatrix = Matrix4x4.CreateScale(particle.Radius, animatedLength, 1);

                // Center the particle at the midpoint between the two points
                var translationMatrix = Matrix4x4.CreateTranslation(Vector3.UnitY * animatedLength);

                // Calculate rotation matrix

                var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, direction));
                var angle = MathF.Acos(direction.Y);
                var rotationMatrix = Matrix4x4.CreateFromAxisAngle(axis, angle);

                var modelMatrix = orientationType == ParticleOrientation.ScreenAligned
                    ? Matrix4x4.Multiply(scaleMatrix, Matrix4x4.Multiply(translationMatrix, rotationMatrix))
                    : particle.GetTransformationMatrix();

                // Position/Radius uniform
                shader.SetUniform4x4("uModelMatrix", modelMatrix);

                if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                {
                    var sequence = spriteSheetData.Sequences[0];

                    var animationTime = animationType switch
                    {
                        ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE => particle.Age,
                        ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particle.NormalizedAge,
                        _ => particle.Age,
                    };
                    var frame = animationTime * sequence.FramesPerSecond * animationRate;

                    var currentFrame = sequence.Frames[(int)MathF.Floor(frame) % sequence.Frames.Length];
                    var currentImage = currentFrame.Images[0]; // TODO: Support more than one image per frame?

                    // Lerp frame coords and size
                    var subFrameTime = frame % 1.0f;
                    var offset = MathUtils.Lerp(subFrameTime, currentImage.CroppedMin, currentImage.UncroppedMin);
                    var scale = MathUtils.Lerp(subFrameTime, currentImage.CroppedMax - currentImage.CroppedMin,
                        currentImage.UncroppedMax - currentImage.UncroppedMin);

                    shader.SetUniform2("uUvOffset", offset);
                    shader.SetUniform2("uUvScale", scale * new Vector2(finalTextureScaleU, finalTextureScaleV));
                }
                else
                {
                    shader.SetUniform2("uUvOffset", Vector2.One);
                    shader.SetUniform2("uUvScale", new Vector2(finalTextureScaleU, finalTextureScaleV));
                }

                // Color uniform
                shader.SetUniform3("uColor", particle.Color);
                shader.SetUniform1("uAlpha", particle.Alpha * particle.AlphaAlternate);

                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            if (additive)
            {
                GL.BlendEquation(BlendEquationMode.FuncAdd);
            }

            GL.Disable(EnableCap.Blend);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
        }

        public IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public void SetRenderMode(string renderMode)
        {
            var parameters = new Dictionary<string, byte>();

            if (renderMode != null && shader.RenderModes.Contains(renderMode))
            {
                parameters.Add($"renderMode_{renderMode}", 1);
            }

            shader = guiContext.ShaderLoader.LoadShader(ShaderName, parameters);
        }
    }
}
