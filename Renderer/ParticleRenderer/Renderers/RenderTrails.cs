using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    internal class RenderTrails : ParticleFunctionRenderer
    {
        private const string ShaderName = "vrf.particle_trail";

        private readonly Shader shader;
        private readonly RendererContext RendererContext;
        private readonly int vaoHandle;
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
        private readonly float lengthFadeInTime;

        public RenderTrails(ParticleDefinitionParser parse, RendererContext rendererContext) : base(parse)
        {
            RendererContext = rendererContext;
            shader = RendererContext.ShaderLoader.LoadShader(ShaderName);

            // The same quad is reused for all particles
            var (quadVao, quadBuffer) = SetupQuadBuffer();
            vaoHandle = quadVao;

            string? textureName = null;

            if (parse.Data.ContainsKey("m_hTexture"))
            {
                textureName = parse.Data.GetProperty<string>("m_hTexture");
            }
            else
            {
                var textures = parse.Array("m_vecTexturesInput");
                if (textures.Length > 0)
                {
                    // TODO: Support more than one texture
                    textureName = textures[0].Data.GetProperty<string>("m_hTexture");
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

            blendMode = parse.Enum<ParticleBlendMode>("m_nOutputBlendMode", blendMode);
            overbrightFactor = parse.NumberProvider("m_flOverbrightFactor", overbrightFactor);
            orientationType = parse.Enum("m_nOrientationType", orientationType);
            animationRate = parse.Float("m_flAnimationRate", animationRate);
            finalTextureScaleU = parse.Float("m_flFinalTextureScaleU", finalTextureScaleU);
            finalTextureScaleV = parse.Float("m_flFinalTextureScaleV", finalTextureScaleV);
            maxLength = parse.Float("m_flMaxLength", maxLength);
            lengthFadeInTime = parse.Float("m_flLengthFadeInTime", lengthFadeInTime);
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

        public override void Render(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix)
        {
            var particles = particleBag.Current;

            if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ADD)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else /* if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA) */
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            shader.Use();

            GL.BindVertexArray(vaoHandle);

            // set texture unit 0 as uTexture uniform
            shader.SetTexture(0, "uTexture", texture);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            // also todo: pass all of these as vertex parameters (probably just color/alpha combined)
            shader.SetUniform1("uOverbrightFactor", (float)overbrightFactor.NextNumber());

            // Create billboarding rotation (always facing camera)
            if (!Matrix4x4.Decompose(modelViewMatrix, out _, out var modelViewRotation, out _))
            {
                throw new InvalidOperationException("Matrix decompose failed");
            }

            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

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

                var modelMatrix = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED
                    ? Matrix4x4.Multiply(scaleMatrix, Matrix4x4.Multiply(translationMatrix, rotationMatrix))
                    : particle.GetTransformationMatrix();

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
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void SetRenderMode(string renderMode)
        {
        }
    }
}
