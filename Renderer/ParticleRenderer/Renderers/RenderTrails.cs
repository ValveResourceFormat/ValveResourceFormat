using System.Buffers;
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
        private const int VertexSize = 9;

        // The shared quad index buffer covers 65532 indices, six per quad
        private const int MaxQuads = 65532 / 6;

        // Quad corners in ring order, matching the winding of the shared quad index buffer
        private static readonly Vector2[] QuadCorners = [new(-1f, -1f), new(-1f, 1f), new(1f, 1f), new(1f, -1f)];

        private readonly Shader shader;
        private readonly RendererContext RendererContext;
        private readonly int vaoHandle;
        private readonly int vertexBufferHandle;
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

            // All trails of this renderer are batched into a single dynamic vertex buffer
            (vaoHandle, vertexBufferHandle) = SetupQuadBuffer();

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
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vertexBufferHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
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

            if (minLength > maxLength)
            {
                // Some particles may have length range set up incorrectly
                maxLength = minLength;
            }
        }

        public override void SetWireframe(bool isWireframe)
        {
            shader.SetUniform1("isWireframe", isWireframe ? 1 : 0);
        }

        private (int Vao, int Buffer) SetupQuadBuffer()
        {
            const int stride = sizeof(float) * VertexSize;

            GL.CreateVertexArrays(1, out int vao);
            GL.CreateBuffers(1, out int buffer);
            GL.VertexArrayVertexBuffer(vao, 0, buffer, 0, stride);
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

            return (vao, buffer);
        }

        /// <summary>
        /// Builds one quad per visible trail into the shared vertex buffer, returning how many were written.
        /// </summary>
        private int UpdateVertices(ParticleCollection particleBag, Camera camera)
        {
            // The moved distance is converted back to a velocity (distance / dt) before scaling by
            // the trail-length attribute, unless the operator opts out of the delta-time division.
            var oneOverDt = ignoreDeltaTime || particleBag.PreviousFrameTime == 0f
                ? 1f
                : 1f / particleBag.PreviousFrameTime;

            var rawVertices = ArrayPool<float>.Shared.Rent(particleBag.Count * VertexSize * 4);
            var quadCount = 0;

            try
            {
                foreach (ref var particle in particleBag.Current)
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

                    var uvOffset = Vector2.Zero;
                    var uvScale = new Vector2(finalTextureScaleU, finalTextureScaleV);

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
                        uvOffset = Vector2.Lerp(currentImage.CroppedMin, currentImage.UncroppedMin, subFrameTime);
                        uvScale *= Vector2.Lerp(currentImage.CroppedMax - currentImage.CroppedMin,
                            currentImage.UncroppedMax - currentImage.UncroppedMin, subFrameTime);
                    }

                    // Corners in index buffer winding order, with the local quad's [-1, 1] axes mapping to [0, 1] uvs
                    var quadStart = quadCount * VertexSize * 4;
                    var alpha = particle.Alpha * particle.AlphaAlternate;

                    for (var j = 0; j < 4; ++j)
                    {
                        var corner = QuadCorners[j];
                        var worldPosition = Vector3.Transform(new Vector3(corner, 0f), modelMatrix);
                        var uv = uvOffset + ((corner * 0.5f) + new Vector2(0.5f)) * uvScale;

                        var vertexStart = quadStart + (VertexSize * j);
                        rawVertices[vertexStart + 0] = worldPosition.X;
                        rawVertices[vertexStart + 1] = worldPosition.Y;
                        rawVertices[vertexStart + 2] = worldPosition.Z;
                        rawVertices[vertexStart + 3] = particle.Color.X;
                        rawVertices[vertexStart + 4] = particle.Color.Y;
                        rawVertices[vertexStart + 5] = particle.Color.Z;
                        rawVertices[vertexStart + 6] = alpha;
                        rawVertices[vertexStart + 7] = uv.X;
                        rawVertices[vertexStart + 8] = uv.Y;
                    }

                    quadCount++;

                    if (quadCount == MaxQuads)
                    {
                        break;
                    }
                }

                if (quadCount > 0)
                {
                    GL.NamedBufferData(vertexBufferHandle, quadCount * VertexSize * 4 * sizeof(float), rawVertices, BufferUsageHint.DynamicDraw);
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(rawVertices);
            }

            return quadCount;
        }

        public override void Render(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            if (particleBag.Count == 0)
            {
                return;
            }

            var quadCount = UpdateVertices(particleBag, camera);

            if (quadCount == 0)
            {
                return;
            }

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
            shader.SetUniform1("uOverbrightFactor", (float)overbrightFactor.NextNumber(systemRenderState));

            PerfStats.Active.Count(Counter.ParticleDraw);
            GL.DrawElements(PrimitiveType.Triangles, quadCount * 6, DrawElementsType.UnsignedShort, 0);

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
