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
        private const string ShaderName = "particle_trail";
        private const int VertexSize = 9;
        private const string DefaultTextureName = "materials/particle/base_trail.vtex";

        // The shared quad index buffer covers 65532 indices, six per quad
        private const int MaxQuads = 65532 / 6;

        // Quad corners in ring order, matching the winding of the shared quad index buffer
        private static readonly Vector2[] QuadCorners = [new(-1f, -1f), new(-1f, 1f), new(1f, 1f), new(1f, -1f)];

        private readonly Shader shader;
        private readonly RendererContext RendererContext;
        private readonly int vaoHandle;
        private readonly int vertexBufferHandle;
        private RenderTexture texture;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly bool animateInFps;

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
        private readonly float constrainRadiusToLengthRatio = 1f;
        private readonly float forwardShift;

        private readonly INumberProvider headRadiusTaper = new LiteralNumberProvider(1f);
        private readonly INumberProvider tailRadiusTaper = new LiteralNumberProvider(1f);
        private readonly INumberProvider headAlphaScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider tailAlphaScale = new LiteralNumberProvider(1f);
        private readonly IVectorProvider headColorScale = new LiteralVectorProvider(Vector3.One);
        private readonly IVectorProvider tailColorScale = new LiteralVectorProvider(Vector3.One);

        public RenderTrails(ParticleDefinitionParser parse, RendererContext rendererContext) : base(parse)
        {
            RendererContext = rendererContext;

            blendMode = parse.Enum<ParticleBlendMode>("m_nOutputBlendMode", blendMode);

            shader = RendererContext.ShaderLoader.LoadShader(ShaderName);

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

            texture = RendererContext.MaterialLoader.GetTexture(textureName ?? DefaultTextureName, srgbRead: true);

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
            constrainRadiusToLengthRatio = parse.Float("m_flConstrainRadiusToLengthRatio", constrainRadiusToLengthRatio);
            forwardShift = parse.Float("m_flForwardShift", forwardShift);
            animationType = parse.Enum<ParticleAnimationType>("m_nAnimationType", animationType);
            animateInFps = parse.Boolean("m_bAnimateInFPS", animateInFps);
            prevPositionSource = parse.ParticleField("m_nPrevPntSource", prevPositionSource);
            headRadiusTaper = parse.NumberProvider("m_flRadiusHeadTaper", headRadiusTaper);
            tailRadiusTaper = parse.NumberProvider("m_flRadiusTaper", tailRadiusTaper);
            headAlphaScale = parse.NumberProvider("m_flHeadAlphaScale", headAlphaScale);
            tailAlphaScale = parse.NumberProvider("m_flTailAlphaScale", tailAlphaScale);
            headColorScale = parse.VectorProvider("m_vecHeadColorScale", headColorScale);
            tailColorScale = parse.VectorProvider("m_vecTailColorScale", tailColorScale);

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

        /// <inheritdoc/>
        public override void SetTextureOverride(RenderTexture texture)
        {
            this.texture = texture;
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
        private int UpdateVertices(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            var headColor = headColorScale.NextVector(systemRenderState);
            var tailColor = tailColorScale.NextVector(systemRenderState);

            // The moved distance is converted back to a velocity (distance / dt) before scaling by
            // the trail-length attribute. The division only applies when the previous point comes
            // from the Verlet pair, and the operator can opt out of it entirely.
            var usesVerletDelta = prevPositionSource == ParticleField.PositionPrevious;
            var oneOverDt = ignoreDeltaTime || !usesVerletDelta || particleBag.CurrentFrameTime == 0f
                ? 1f
                : 1f / particleBag.CurrentFrameTime;

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

                    // A short trail is narrowed so it cannot render wider than it is long
                    var radius = MathF.Min(particle.Radius, constrainRadiusToLengthRatio * length);

                    Vector3 center;
                    Vector3 widthAxis;
                    Vector3 lengthAxis;
                    float halfWidth;
                    float halfLength;

                    if (orientationType == ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED)
                    {
                        // The quad's width axis stays perpendicular to the eye ray, its length axis follows the motion
                        widthAxis = Vector3.Cross(position - camera.Location, direction);
                        widthAxis = widthAxis.LengthSquared() > 1e-12f
                            ? Vector3.Normalize(widthAxis)
                            : Vector3.Normalize(Vector3.Cross(direction, MathF.Abs(direction.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX));

                        lengthAxis = direction;
                        halfWidth = radius * 0.5f;
                        halfLength = length * 0.5f;

                        // The engine slides the trail along the motion axis by m_flForwardShift lengths;
                        // direction runs backwards along travel here, so the shift subtracts
                        center = position + (direction * (length * (0.5f - forwardShift)));
                    }
                    else
                    {
                        // TODO: Other orientation types render as plain unstretched sprites here; the engine
                        // still stretches them along the motion, constrained to the ground/normal plane
                        center = position;
                        widthAxis = Vector3.UnitX;
                        lengthAxis = Vector3.UnitY;
                        halfWidth = particle.Radius;
                        halfLength = particle.Radius;
                    }

                    var headHalfWidth = halfWidth * headRadiusTaper.NextNumber(ref particle, systemRenderState);
                    var tailHalfWidth = halfWidth * tailRadiusTaper.NextNumber(ref particle, systemRenderState);

                    var uvOffset = Vector2.Zero;
                    var uvScale = new Vector2(finalTextureScaleU, finalTextureScaleV);

                    var spriteSheetData = texture.SpriteSheetData;
                    if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                    {
                        var sequence = spriteSheetData.Sequences[particle.Sequence % spriteSheetData.Sequences.Length];
                        var (frame, _, _) = GetSheetFrame(ref particle, sequence, animationRate, animationType, animateInFps);

                        // TODO: Support more than one image per frame?
                        var currentImage = sequence.Frames[frame].Images[0];

                        uvOffset = currentImage.UncroppedMin;
                        uvScale *= currentImage.UncroppedMax - currentImage.UncroppedMin;
                    }

                    // Corners in index buffer winding order, with the local quad's [-1, 1] axes mapping to [0, 1] uvs
                    var quadStart = quadCount * VertexSize * 4;
                    var alpha = particle.Alpha * particle.AlphaAlternate;

                    var head = Vector4.Clamp(
                        new Vector4(particle.Color * headColor, alpha * headAlphaScale.NextNumber(ref particle, systemRenderState)),
                        Vector4.Zero, Vector4.One);
                    var tail = Vector4.Clamp(
                        new Vector4(particle.Color * tailColor, alpha * tailAlphaScale.NextNumber(ref particle, systemRenderState)),
                        Vector4.Zero, Vector4.One);

                    for (var j = 0; j < 4; ++j)
                    {
                        var corner = QuadCorners[j];
                        var isHead = corner.Y < 0f;
                        var worldPosition = center
                            + (widthAxis * (corner.X * (isHead ? headHalfWidth : tailHalfWidth)))
                            + (lengthAxis * (corner.Y * halfLength));
                        var color = isHead ? head : tail;
                        var uv = uvOffset + ((corner * 0.5f) + new Vector2(0.5f)) * uvScale;

                        var vertexStart = quadStart + (VertexSize * j);
                        rawVertices[vertexStart + 0] = worldPosition.X;
                        rawVertices[vertexStart + 1] = worldPosition.Y;
                        rawVertices[vertexStart + 2] = worldPosition.Z;
                        rawVertices[vertexStart + 3] = color.X;
                        rawVertices[vertexStart + 4] = color.Y;
                        rawVertices[vertexStart + 5] = color.Z;
                        rawVertices[vertexStart + 6] = color.W;
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

            var quadCount = UpdateVertices(particleBag, systemRenderState, camera);

            if (quadCount == 0)
            {
                return;
            }

            // The translucent pass leaves blend/depth state to each custom draw; enable blending and stop depth
            // writes here or trails render opaque (matching the sprite renderer; cables draw opaque with depth writes instead).
            GL.Enable(EnableCap.Blend);
            GL.DepthMask(false);

            // MOD2X adds like ADD does; spritecard has no blend state that scales the destination.
            if (blendMode is ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ADD
                or ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X)
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

            // Set every draw: the program is shared with every other trail renderer, whatever their mode.
            shader.SetUniform1("uBlendMode", (int)blendMode);

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
