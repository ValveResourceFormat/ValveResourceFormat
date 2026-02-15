using System.Buffers;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    internal class RenderSprites : ParticleFunctionRenderer
    {
        private const string ShaderName = "vrf.particle_sprite";
        private const int VertexSize = 9;

        private readonly Shader shader;
        private readonly RendererContext RendererContext;
        private readonly int vaoHandle;
        private readonly RenderTexture texture;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly INumberProvider minSize = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxSize = new LiteralNumberProvider(5000f);

        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider alphaScale = new LiteralNumberProvider(1f);

        private readonly bool animateInFps;
        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly INumberProvider overbrightFactor = new LiteralNumberProvider(1);
        private readonly ParticleOrientation orientationType;
        private int vertexBufferHandle;


        public RenderSprites(ParticleDefinitionParser parse, RendererContext rendererContext) : base(parse)
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
            vaoHandle = SetupQuadBuffer();

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
                texture = rendererContext.MaterialLoader.GetErrorTexture();
            }
            else
            {
                texture = rendererContext.MaterialLoader.GetTexture(textureName, srgbRead: true);
            }

#if DEBUG
            var vaoLabel = $"{nameof(RenderSprites)}: {System.IO.Path.GetFileName(textureName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vertexBufferHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
#endif

            animateInFps = parse.Boolean("m_bAnimateInFPS", animateInFps);
            overbrightFactor = parse.NumberProvider("m_flOverbrightFactor", overbrightFactor);
            orientationType = parse.Enum("m_nOrientationType", orientationType);
            animationRate = parse.Float("m_flAnimationRate", animationRate);
            minSize = parse.NumberProvider("m_flMinSize", minSize);
            maxSize = parse.NumberProvider("m_flMaxSize", maxSize);
            animationType = parse.Enum<ParticleAnimationType>("m_nAnimationType", animationType);
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            alphaScale = parse.NumberProvider("m_flAlphaScale", alphaScale);
        }

        public override void SetWireframe(bool isWireframe)
        {
            // Solid color
            shader.SetUniform1("isWireframe", isWireframe ? 1 : 0);
        }

        private int SetupQuadBuffer()
        {
            const int stride = sizeof(float) * VertexSize;

            GL.CreateVertexArrays(1, out int vao);
            GL.CreateBuffers(1, out vertexBufferHandle);
            GL.VertexArrayVertexBuffer(vao, 0, vertexBufferHandle, 0, stride);
            GL.VertexArrayElementBuffer(vao, RendererContext.MeshBufferCache.QuadIndices.GLHandle);

            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            var colorAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexColor");
            var uvAttributeLocation = GL.GetAttribLocation(shader.Program, "aTexCoords");

            GL.EnableVertexArrayAttrib(vao, positionAttributeLocation);
            GL.EnableVertexArrayAttrib(vao, colorAttributeLocation);
            GL.EnableVertexArrayAttrib(vao, uvAttributeLocation);

            GL.VertexArrayAttribFormat(vao, positionAttributeLocation, 3, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribFormat(vao, colorAttributeLocation, 4, VertexAttribType.Float, false, sizeof(float) * 3);
            GL.VertexArrayAttribFormat(vao, uvAttributeLocation, 2, VertexAttribType.Float, false, sizeof(float) * 7);

            GL.VertexArrayAttribBinding(vao, positionAttributeLocation, 0);
            GL.VertexArrayAttribBinding(vao, colorAttributeLocation, 0);
            GL.VertexArrayAttribBinding(vao, uvAttributeLocation, 0);

            return vao;
        }

        private void UpdateVertices(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix)
        {
            // Create billboarding rotation (always facing camera)
            if (!Matrix4x4.Decompose(modelViewMatrix, out _, out var modelViewRotation, out _))
            {
                throw new InvalidOperationException("Matrix decompose failed");
            }

            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            // Update vertex buffer
            var rawVertices = ArrayPool<float>.Shared.Rent(particles.Count * VertexSize * 4);

            try
            {
                var i = 0;
                foreach (ref var particle in particles.Current)
                {
                    var radiusScale = this.radiusScale.NextNumber(ref particle, systemRenderState);

                    // Positions
                    var modelMatrix = orientationType switch
                    {
                        ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL or
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL or
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED => particle.GetRotationMatrix() * billboardMatrix * particle.GetTransformationMatrix(radiusScale),
                        _ => particle.GetRotationMatrix() * particle.GetTransformationMatrix(radiusScale),
                    };

                    var tl = Vector4.Transform(new Vector4(-1, -1, 0, 1), modelMatrix);
                    var bl = Vector4.Transform(new Vector4(-1, 1, 0, 1), modelMatrix);
                    var br = Vector4.Transform(new Vector4(1, 1, 0, 1), modelMatrix);
                    var tr = Vector4.Transform(new Vector4(1, -1, 0, 1), modelMatrix);

                    var quadStart = i * VertexSize * 4;
                    rawVertices[quadStart + 0] = tl.X;
                    rawVertices[quadStart + 1] = tl.Y;
                    rawVertices[quadStart + 2] = tl.Z;
                    rawVertices[quadStart + (VertexSize * 1) + 0] = bl.X;
                    rawVertices[quadStart + (VertexSize * 1) + 1] = bl.Y;
                    rawVertices[quadStart + (VertexSize * 1) + 2] = bl.Z;
                    rawVertices[quadStart + (VertexSize * 2) + 0] = br.X;
                    rawVertices[quadStart + (VertexSize * 2) + 1] = br.Y;
                    rawVertices[quadStart + (VertexSize * 2) + 2] = br.Z;
                    rawVertices[quadStart + (VertexSize * 3) + 0] = tr.X;
                    rawVertices[quadStart + (VertexSize * 3) + 1] = tr.Y;
                    rawVertices[quadStart + (VertexSize * 3) + 2] = tr.Z;

                    var alphaScale = this.alphaScale.NextNumber(ref particle, systemRenderState);
                    // Colors
                    for (var j = 0; j < 4; ++j)
                    {
                        rawVertices[quadStart + (VertexSize * j) + 3] = particle.Color.X;
                        rawVertices[quadStart + (VertexSize * j) + 4] = particle.Color.Y;
                        rawVertices[quadStart + (VertexSize * j) + 5] = particle.Color.Z;
                        rawVertices[quadStart + (VertexSize * j) + 6] = particle.Alpha * alphaScale;
                    }

                    // UVs
                    var spriteSheetData = texture.SpriteSheetData;
                    if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                    {
                        var sequence = spriteSheetData.Sequences[particle.Sequence % spriteSheetData.Sequences.Length];

                        var animationTime = animationType switch
                        {
                            ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE => particle.Age,
                            ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particle.NormalizedAge,
                            ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES => particle.Age, // literally dont know what to do with this one
                            _ => particle.Age,
                        };

                        var frameId = 0;

                        if (sequence.Frames.Length > 1)
                        {
                            if (animateInFps)
                            {
                                frameId = (int)(animationRate * animationTime);
                            }
                            else
                            {
                                frameId = (int)(animationTime * animationRate * sequence.FramesPerSecond);
                            }

                            if (sequence.Clamp)
                            {
                                frameId = Math.Min(frameId, sequence.Frames.Length - 1);
                            }
                            else
                            {
                                frameId %= sequence.Frames.Length;
                            }
                        }

                        var currentFrame = sequence.Frames[frameId];
                        var currentImage = currentFrame.Images[0]; // TODO: Support more than one image per frame?

                        // Lerp frame coords and size
                        var offset = currentImage.UncroppedMin;
                        var scale = currentImage.UncroppedMax - currentImage.UncroppedMin;

                        rawVertices[quadStart + (VertexSize * 0) + 7] = offset.X;
                        rawVertices[quadStart + (VertexSize * 0) + 8] = offset.Y + scale.Y;
                        rawVertices[quadStart + (VertexSize * 1) + 7] = offset.X;
                        rawVertices[quadStart + (VertexSize * 1) + 8] = offset.Y;
                        rawVertices[quadStart + (VertexSize * 2) + 7] = offset.X + scale.X;
                        rawVertices[quadStart + (VertexSize * 2) + 8] = offset.Y;
                        rawVertices[quadStart + (VertexSize * 3) + 7] = offset.X + scale.X;
                        rawVertices[quadStart + (VertexSize * 3) + 8] = offset.Y + scale.Y;
                    }
                    else
                    {
                        rawVertices[quadStart + (VertexSize * 0) + 7] = 0;
                        rawVertices[quadStart + (VertexSize * 0) + 8] = 1;
                        rawVertices[quadStart + (VertexSize * 1) + 7] = 0;
                        rawVertices[quadStart + (VertexSize * 1) + 8] = 0;
                        rawVertices[quadStart + (VertexSize * 2) + 7] = 1;
                        rawVertices[quadStart + (VertexSize * 2) + 8] = 0;
                        rawVertices[quadStart + (VertexSize * 3) + 7] = 1;
                        rawVertices[quadStart + (VertexSize * 3) + 8] = 1;
                    }

                    i++;
                }

                GL.NamedBufferData(vertexBufferHandle, particles.Count * VertexSize * 4 * sizeof(float), rawVertices, BufferUsageHint.DynamicDraw);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(rawVertices);
            }
        }

        public override void Render(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Matrix4x4 modelViewMatrix)
        {
            if (particleBag.Count == 0)
            {
                return;
            }

            // Update vertex buffer
            UpdateVertices(particleBag, systemRenderState, modelViewMatrix);

            // Draw it
            if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ADD)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X)
            {
                GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
            }
            else /* if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA) */
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            GL.Disable(EnableCap.CullFace);

            shader.Use();
            GL.BindVertexArray(vaoHandle);

            // set texture unit 0 as uTexture uniform
            shader.SetTexture(0, "uTexture", texture);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            shader.SetUniform1("uOverbrightFactor", overbrightFactor.NextNumber());

            // DRAW
            GL.DrawElements(PrimitiveType.Triangles, particleBag.Count * 6, DrawElementsType.UnsignedShort, 0);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.Enable(EnableCap.CullFace);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void SetRenderMode(string renderMode)
        {
        }
    }
}
