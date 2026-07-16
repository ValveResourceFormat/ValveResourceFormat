using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders particles as trail segments stretched between the particle's current and previous
    /// positions, with configurable length, fade-in, texture scaling, and blend modes.
    /// </summary>
    /// <remarks>
    /// Trails are sprites that stretch based on their speed over time. Traditional use cases
    /// include bullet tracers and sparks; they are also useful when particles need to be oriented
    /// in 3D space, which regular sprites handle poorly.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderTrails">C_OP_RenderTrails</seealso>
    internal class RenderTrails : ParticleFunctionRenderer
    {
        private const string ShaderName = "vrf.particle_trail";

        private readonly Shader shader;
        private readonly RendererContext RendererContext;
        private readonly int vaoHandle;
        private readonly int bufferHandle;
        private readonly RenderTexture texture;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;

        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly INumberProvider overbrightFactor = new LiteralNumberProvider(1);
        private readonly ParticleOrientation orientationType;
        private readonly ParticleField prevPositionSource = ParticleField.PositionPrevious; // this is a real thing

        private readonly float finalTextureScaleU = 1f;
        private readonly float finalTextureScaleV = 1f;

        private readonly float maxLength = 2000f;
        private readonly float minLength;
        private readonly float lengthScale = 1f;
        private readonly float lengthFadeInTime;
        private readonly bool ignoreDeltaTime;

        public RenderTrails(ParticleDefinitionParser parse, RendererContext rendererContext) : base(parse)
        {
            RendererContext = rendererContext;

            blendMode = parse.Enum<ParticleBlendMode>("m_nOutputBlendMode", blendMode);

            var shaderParams = new Dictionary<string, byte>();
            if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ADD)
            {
                shaderParams["F_ADDITIVE_BLEND"] = 1;
            }
            else if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X)
            {
                shaderParams["F_MOD2X"] = 1;
            }

            shader = RendererContext.ShaderLoader.LoadShader(ShaderName, shaderParams);

            // The same quad is reused for all particles
            var (quadVao, quadBuffer) = SetupQuadBuffer();
            vaoHandle = quadVao;
            bufferHandle = quadBuffer;

            string? textureName = null;

            if (parse.Data.ContainsKey("m_hTexture"))
            {
                textureName = parse.Data.GetStringProperty("m_hTexture");
            }
            else
            {
                var textures = parse.Array("m_vecTexturesInput");
                if (textures.Length > 0)
                {
                    // TODO: Support more than one texture
                    textureName = textures[0].Data.GetStringProperty("m_hTexture");
                }
            }

            if (textureName == null)
            {
                texture = RendererContext.MaterialLoader.GetErrorTexture();
            }
            else
            {
                texture = RendererContext.MaterialLoader.GetTexture(textureName, srgbRead: true);
            }

#if DEBUG
            var vaoLabel = $"{nameof(RenderTrails)}: {System.IO.Path.GetFileName(textureName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, quadBuffer, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
#endif

            overbrightFactor = parse.NumberProvider("m_flOverbrightFactor", overbrightFactor);
            orientationType = parse.Enum("m_nOrientationType", orientationType);
            animationRate = parse.Float("m_flAnimationRate", animationRate);
            finalTextureScaleU = parse.Float("m_flFinalTextureScaleU", finalTextureScaleU);
            finalTextureScaleV = parse.Float("m_flFinalTextureScaleV", finalTextureScaleV);
            maxLength = parse.Float("m_flMaxLength", maxLength);
            minLength = parse.Float("m_flMinLength", minLength);
            lengthScale = parse.Float("m_flLengthScale", lengthScale);
            lengthFadeInTime = parse.Float("m_flLengthFadeInTime", lengthFadeInTime);
            ignoreDeltaTime = parse.Boolean("m_bIgnoreDT", ignoreDeltaTime);
            animationType = parse.Enum<ParticleAnimationType>("m_nAnimationType", animationType);
            prevPositionSource = parse.ParticleField("m_nPrevPntSource", prevPositionSource);
        }

        public override void SetWireframe(bool isWireframe)
        {
            shader.SetUniform1("isWireframe", isWireframe ? 1 : 0);
        }

        private (int Vao, int Buffer) SetupQuadBuffer()
        {
            var vertices = new[]
            {
                -1.0f, -1.0f, 0.0f,
                -1.0f, 1.0f, 0.0f,
                1.0f, -1.0f, 0.0f,
                1.0f, 1.0f, 0.0f,
            };

            GL.CreateVertexArrays(1, out int vao);
            GL.CreateBuffers(1, out int buffer);
            GL.NamedBufferData(buffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.VertexArrayVertexBuffer(vao, 0, buffer, 0, sizeof(float) * 3);

            var attributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexArrayAttrib(vao, attributeLocation);
            GL.VertexArrayAttribFormat(vao, attributeLocation, 3, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribBinding(vao, attributeLocation, 0);

            return (vao, buffer);
        }

        public override void Render(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            var particles = particleBag.Current;

            // The translucent pass leaves blend/depth state to each custom draw; enable blending and stop depth
            // writes here or trails render opaque (matching the sprite renderer; cables draw opaque with depth writes instead).
            GL.Enable(EnableCap.Blend);
            GL.DepthMask(false);

            if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ADD)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else /* if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA) */
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            // Trail quads are oriented by motion direction, so either side can face the camera
            GL.Disable(EnableCap.CullFace);

            shader.Use();

            GL.BindVertexArray(vaoHandle);

            shader.SetTexture(RenderMaterial.TextureUnitStart, "uTexture", texture);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            // also todo: pass all of these as vertex parameters (probably just color/alpha combined)
            shader.SetUniform1("uOverbrightFactor", (float)overbrightFactor.NextNumber(systemRenderState));

            // The moved distance is converted back to a velocity (distance / dt) before scaling by
            // the trail-length attribute, unless the operator opts out of the delta-time division.
            var oneOverDt = ignoreDeltaTime || particleBag.PreviousFrameTime == 0f
                ? 1f
                : 1f / particleBag.PreviousFrameTime;

            // Todo: this could be adapted into renderropes without much difficulty
            foreach (ref var particle in particles)
            {
                var position = particle.Position;
                var previousPosition = particle.GetVector(prevPositionSource);
                // The trail extends from the particle back toward its previous position
                var difference = previousPosition - position;
                var direction = difference == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(difference);

                var length = lengthScale * particle.TrailLength * difference.Length() * oneOverDt;

                // The length fades in before clamping so clamped trails still reach full length on time
                if (particle.Age < lengthFadeInTime)
                {
                    length *= particle.Age / lengthFadeInTime;
                }

                if (length <= 0f)
                {
                    continue;
                }

                // The engine clamps the full extent of the trail
                length = Math.Clamp(length, minLength, maxLength);

                Matrix4x4 modelMatrix;
                if (orientationType == ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED)
                {
                    // The quad's width axis stays perpendicular to the eye ray, its length axis follows the motion
                    var widthAxis = Vector3.Cross(position - camera.Location, direction);
                    widthAxis = widthAxis.LengthSquared() > 1e-12f
                        ? Vector3.Normalize(widthAxis)
                        : Vector3.Normalize(Vector3.Cross(direction, MathF.Abs(direction.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX));
                    var normal = Vector3.Cross(widthAxis, direction);

                    var halfWidth = particle.Radius * 0.5f;
                    var halfLength = length * 0.5f;
                    var center = position + direction * halfLength;

                    modelMatrix = new Matrix4x4(
                        widthAxis.X * halfWidth, widthAxis.Y * halfWidth, widthAxis.Z * halfWidth, 0f,
                        direction.X * halfLength, direction.Y * halfLength, direction.Z * halfLength, 0f,
                        normal.X, normal.Y, normal.Z, 0f,
                        center.X, center.Y, center.Z, 1f);
                }
                else
                {
                    // TODO: Other orientation types render as plain unstretched sprites here; the engine
                    // still stretches them along the motion, constrained to the ground/normal plane
                    modelMatrix = particle.GetTransformationMatrix();
                }

                // Position/Radius uniform
                shader.SetUniform4x4("uModelMatrix", modelMatrix);

                var spriteSheetData = texture.SpriteSheetData;
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
                    var offset = Vector2.Lerp(currentImage.CroppedMin, currentImage.UncroppedMin, subFrameTime);
                    var scale = Vector2.Lerp(currentImage.CroppedMax - currentImage.CroppedMin,
                        currentImage.UncroppedMax - currentImage.UncroppedMin, subFrameTime);

                    shader.SetUniform2("uUvOffset", offset);
                    shader.SetUniform2("uUvScale", scale * new Vector2(finalTextureScaleU, finalTextureScaleV));
                }
                else
                {
                    shader.SetUniform2("uUvOffset", Vector2.Zero);
                    shader.SetUniform2("uUvScale", new Vector2(finalTextureScaleU, finalTextureScaleV));
                }

                // Color uniform
                shader.SetUniform3("uColor", particle.Color);
                shader.SetUniform1("uAlpha", particle.Alpha * particle.AlphaAlternate);

                Counters.Active.Count(Counter.ParticleDraw);
                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.Enable(EnableCap.CullFace);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void SetRenderMode(string renderMode)
        {
        }

        public override void Delete()
        {
            GL.DeleteVertexArray(vaoHandle);
            GL.DeleteBuffer(bufferHandle);
        }
    }
}
