using System.Buffers;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders particles as camera-facing or orientation-aligned textured quads (sprites),
    /// with support for sprite sheet animation, blend modes, and per-particle color and alpha.
    /// </summary>
    /// <remarks>
    /// The workhorse renderer used by most effects. Multi-frame sequences can be animated or
    /// used to provide visual variation.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderSprites">C_OP_RenderSprites</seealso>
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
        private readonly SpriteCardTextureChannel textureChannels = SpriteCardTextureChannel.SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA;

        private readonly bool animateInFps;
        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly INumberProvider overbrightFactor = new LiteralNumberProvider(1);
        private readonly ParticleOrientation orientationType;
        private readonly INumberProvider diffuseAmount = new LiteralNumberProvider(1);
        private readonly INumberProvider selfIllumAmount = new LiteralNumberProvider(0);
        private readonly INumberProvider alphaMapToZero = new LiteralNumberProvider(0);
        private readonly INumberProvider alphaMapToOne = new LiteralNumberProvider(1);
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
                textureName = parse.Data.GetStringProperty("m_hTexture");
            }
            else
            {
                // TODO: Support more than one texture
                foreach (var textureInput in parse.Array("m_vecTexturesInput"))
                {
                    if (!textureInput.Boolean("m_bEnabled", true))
                    {
                        continue;
                    }

                    textureName = textureInput.Data.GetStringProperty("m_hTexture");
                    textureChannels = textureInput.Enum("m_nTextureChannels", textureChannels);
                    break;
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
            diffuseAmount = parse.NumberProvider("m_flDiffuseAmount", diffuseAmount);
            selfIllumAmount = parse.NumberProvider("m_flSelfIllumAmount", selfIllumAmount);
            alphaMapToZero = parse.NumberProvider("m_flSourceAlphaValueToMapToZero", alphaMapToZero);
            alphaMapToOne = parse.NumberProvider("m_flSourceAlphaValueToMapToOne", alphaMapToOne);
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

        // A quad orientation matrix from a base (right, up) pair with the particle roll folded in, matching the
        // spritecard vertex shader. The axes are intentionally not re-normalized (some modes rely on that, e.g.
        // SCREEN_Z foreshortens as the camera tilts). The face row is only the normal and does not affect corners.
        private static Matrix4x4 QuadBasis(Vector3 baseRight, Vector3 baseUp, float roll)
        {
            var c = MathF.Cos(roll);
            var s = MathF.Sin(roll);
            var right = (baseRight * c) + (baseUp * s);
            var up = (baseUp * c) - (baseRight * s);
            var face = Vector3.Cross(right, up);
            face = face.LengthSquared() > 1e-12f ? Vector3.Normalize(face) : Vector3.UnitZ;
            return new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                face.X, face.Y, face.Z, 0f,
                0f, 0f, 0f, 1f);
        }

        // World-space camera forward (into the scene): the billboard maps local +Z to the toward-camera axis.
        private static Vector3 CameraForward(Matrix4x4 billboard)
            => -new Vector3(billboard.M31, billboard.M32, billboard.M33);

        // SCREEN_Z_ALIGNED: up locked to world +Z, right = cross(worldZ, forward) left un-normalized, so the
        // sprite yaws about vertical to face the camera and foreshortens as the view tilts off-horizontal.
        private static Matrix4x4 ScreenZAlignedBasis(Matrix4x4 billboard, float roll)
            => QuadBasis(Vector3.Cross(Vector3.UnitZ, CameraForward(billboard)), Vector3.UnitZ, roll);

        // WORLD_Z_ALIGNED: the quad lies flat in the world XY plane (normal = +Z), rolling about vertical,
        // independent of the camera.
        private static Matrix4x4 WorldZAlignedBasis(float roll)
            => QuadBasis(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), roll);

        // ALIGN_TO_PARTICLE_NORMAL: quad plane perpendicular to the particle normal, with the shader's canonical
        // tangent frame (reference axis chosen away from the normal).
        private static Matrix4x4 ParticleNormalBasis(Vector3 normal, float roll)
        {
            var reference = MathF.Abs(normal.Z) > 0.9f ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 0f, 1f);
            var tangent = Vector3.Normalize(Vector3.Cross(normal, reference));
            var bitangent = Vector3.Cross(normal, tangent);
            return QuadBasis(bitangent, tangent, roll);
        }

        // SCREENALIGN_TO_PARTICLE_NORMAL: the quad's right edge follows the particle normal while it turns toward
        // the camera about that normal. Falls back to a billboard when the normal points at the camera.
        private static Matrix4x4 ScreenAlignToNormalBasis(Matrix4x4 billboard, Vector3 normal, float roll)
        {
            var n = Vector3.Normalize(normal);
            var w = Vector3.Cross(n, CameraForward(billboard));
            if (w.LengthSquared() < 1e-8f)
            {
                return billboard;
            }

            return QuadBasis(n, Vector3.Normalize(w), roll);
        }

        private void UpdateVertices(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            var modelViewMatrix = camera.CameraViewMatrix;

            // Create billboarding rotation (always facing camera)
            if (!Matrix4x4.Decompose(modelViewMatrix, out _, out var modelViewRotation, out _))
            {
                throw new InvalidOperationException("Matrix decompose failed");
            }

            modelViewRotation = Quaternion.Inverse(modelViewRotation);
            var billboardMatrix = Matrix4x4.CreateFromQuaternion(modelViewRotation);

            // Screen-size clamps: m_flMinSize/m_flMaxSize are fractions of the screen a sprite may cover;
            // tiny flashes rely on the minimum to stay visible at any camera distance.
            var minScreenSize = minSize.NextNumber(systemRenderState);
            var maxScreenSize = maxSize.NextNumber(systemRenderState);
            var tanHalfFov = MathF.Tan(camera.GetFOV() * 0.5f);

            // Update vertex buffer
            var rawVertices = ArrayPool<float>.Shared.Rent(particles.Count * VertexSize * 4);

            try
            {
                var i = 0;
                foreach (ref var particle in particles.Current)
                {
                    var radiusScale = this.radiusScale.NextNumber(ref particle, systemRenderState);

                    var distanceToCamera = Vector3.Distance(camera.Location, particle.Position);
                    if (distanceToCamera > 1e-3f && tanHalfFov > 0f)
                    {
                        var screenHalfHeight = distanceToCamera * tanHalfFov;
                        var screenFraction = particle.Radius * radiusScale / screenHalfHeight;
                        if (screenFraction < minScreenSize && screenFraction > 0f)
                        {
                            radiusScale *= minScreenSize / screenFraction;
                        }
                        else if (screenFraction > maxScreenSize)
                        {
                            radiusScale *= maxScreenSize / screenFraction;
                        }
                    }

                    // Per-mode quad orientation, ported from the spritecard vertex shader (roll = Rotation.Z).
                    // SCREEN_ALIGNED is the plain camera billboard; FULL_3AXIS_ROTATION has no shader variant and
                    // uses the particle's full rotation basis.
                    var roll = particle.Rotation.Z;
                    var modelMatrix = orientationType switch
                    {
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED => particle.GetRotationMatrix() * billboardMatrix * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_Z_ALIGNED => ScreenZAlignedBasis(billboardMatrix, roll) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => WorldZAlignedBasis(roll) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => ParticleNormalBasis(particle.Normal, roll) * particle.GetTransformationMatrix(radiusScale),
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL => ScreenAlignToNormalBasis(billboardMatrix, particle.Normal, roll) * particle.GetTransformationMatrix(radiusScale),
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

                        var frameId = 0;

                        if (sequence.Frames.Length > 1)
                        {
                            if (animationType == ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES)
                            {
                                frameId = particle.ManualAnimationFrame;
                            }
                            else if (animateInFps)
                            {
                                frameId = (int)(animationRate * particle.Age);
                            }
                            else
                            {
                                var animationTime = animationType switch
                                {
                                    ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE => particle.Age,
                                    ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particle.NormalizedAge,
                                    _ => particle.Age,
                                };

                                frameId = (int)(animationTime * animationRate * sequence.FramesPerSecond);
                            }

                            if (sequence.Clamp)
                            {
                                frameId = Math.Clamp(frameId, 0, sequence.Frames.Length - 1);
                            }
                            else
                            {
                                frameId %= sequence.Frames.Length;
                                if (frameId < 0)
                                {
                                    frameId += sequence.Frames.Length;
                                }
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

        public override void Render(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            if (particleBag.Count == 0)
            {
                return;
            }

            // Update vertex buffer
            UpdateVertices(particleBag, systemRenderState, camera);

            // Draw it. The translucent pass leaves blend/depth state to each custom draw, so enable blending and
            // stop depth writes here; otherwise sprites are opaque. The cable renderer instead draws opaque with depth writes.
            GL.Enable(EnableCap.Blend);
            GL.DepthMask(false);

            if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X)
            {
                GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
            }
            else
            {
                // Premultiplied output; the shader zeroes the blend weight for additive.
                GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
            }

            GL.Disable(EnableCap.CullFace);

            shader.Use();
            GL.BindVertexArray(vaoHandle);

            shader.SetTexture(RenderMaterial.TextureUnitStart, "uTexture", texture);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            shader.SetUniform1("uOverbrightFactor", overbrightFactor.NextNumber(systemRenderState));
            shader.SetUniform1("uColorFactor", diffuseAmount.NextNumber(systemRenderState) + selfIllumAmount.NextNumber(systemRenderState));

            var mapToZero = alphaMapToZero.NextNumber(systemRenderState);
            var alphaRemapRange = alphaMapToOne.NextNumber(systemRenderState) - mapToZero;
            var alphaRemapScaleBias = MathF.Abs(alphaRemapRange) > 0.0001f
                ? new Vector2(1f / alphaRemapRange, -mapToZero / alphaRemapRange)
                : new Vector2(1f, 0f);
            shader.SetUniform2("uAlphaRemapScaleBias", alphaRemapScaleBias);
            shader.SetUniform1("uTextureChannels", (int)textureChannels);

            // DRAW
            PerfStats.Active.Count(Counter.ParticleDraw);
            GL.DrawElements(PrimitiveType.Triangles, particleBag.Count * 6, DrawElementsType.UnsignedShort, 0);

            GL.Enable(EnableCap.CullFace);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void SetRenderMode(string renderMode)
        {
        }

        public override void Delete()
        {
            GL.DeleteVertexArray(vaoHandle);
            GL.DeleteBuffer(vertexBufferHandle);
        }
    }
}
