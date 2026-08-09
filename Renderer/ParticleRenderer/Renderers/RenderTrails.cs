using System.Buffers;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Renderer.Particles.Utils;

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
        // position 3, colour 4, uv 2, next-frame uv 2, frame blend 1
        private const int VertexSize = 12;
        private const string DefaultTextureName = "materials/particle/base_trail.vtex";

        // The shared quad index buffer covers 65532 indices, six per quad
        private const int MaxQuads = 65532 / 6;

        // Quad corners in ring order, matching the winding of the shared quad index buffer
        private static readonly Vector2[] QuadCorners = [new(-1f, -1f), new(-1f, 1f), new(1f, 1f), new(1f, -1f)];

        private readonly Shader shader;
        private readonly RendererContext rendererContext;
        private readonly int vaoHandle;
        private readonly int vertexBufferHandle;
        private RenderTexture texture;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly bool animateInFps;
        private readonly bool blendFrames = true;

        // m_bEnableFadingAndClamping gates the size clamp, the distance fade and the view angle fade
        // together. The bounds below are trail-specific and differ from the sprite renderer's.
        private readonly bool enableFadingAndClamping;
        private readonly float minSize;
        private readonly float maxSize = 2000f;
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(1000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(2000f);
        private readonly float startFadeDot = 1f;
        private readonly float endFadeDot = 2f;

        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
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
            this.rendererContext = rendererContext;

            blendMode = parse.Enum<ParticleBlendMode>("m_nOutputBlendMode", blendMode);

            shader = rendererContext.ShaderLoader.LoadShader(ShaderName);

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

            texture = rendererContext.MaterialLoader.GetTexture(textureName ?? DefaultTextureName, srgbRead: true);

#if DEBUG
            var vaoLabel = $"{nameof(RenderTrails)}: {System.IO.Path.GetFileName(textureName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vertexBufferHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
#endif

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
            blendFrames = parse.Boolean("m_bBlendFramesSeq0", blendFrames);
            enableFadingAndClamping = parse.Boolean("m_bEnableFadingAndClamping", enableFadingAndClamping);
            minSize = parse.Float("m_flMinSize", minSize);
            maxSize = parse.Float("m_flMaxSize", maxSize);
            startFadeSize = parse.NumberProvider("m_flStartFadeSize", startFadeSize);
            endFadeSize = parse.NumberProvider("m_flEndFadeSize", endFadeSize);
            startFadeDot = parse.Float("m_flStartFadeDot", startFadeDot);
            endFadeDot = parse.Float("m_flEndFadeDot", endFadeDot);
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
            GL.VertexArrayElementBuffer(vao, rendererContext.MeshBufferCache.QuadIndices.GLHandle);

            // A driver is free to drop an attribute whose only use sits behind a uniform branch, in which
            // case GetAttribLocation reports -1 and binding it would raise a GL error.
            void SetupAttribute(string name, int components, int offsetInFloats)
            {
                var location = GL.GetAttribLocation(shader.Program, name);

                if (location < 0)
                {
                    return;
                }

                GL.EnableVertexArrayAttrib(vao, location);
                GL.VertexArrayAttribFormat(vao, location, components, VertexAttribType.Float, false, sizeof(float) * offsetInFloats);
                GL.VertexArrayAttribBinding(vao, location, 0);
            }

            SetupAttribute("aVertexPosition", 3, 0);
            SetupAttribute("aVertexColor", 4, 3);
            SetupAttribute("aTexCoords", 2, 7);
            SetupAttribute("aTexCoordsNextFrame", 2, 9);
            SetupAttribute("aFrameBlend", 1, 11);

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

            // Both fade bounds are a radius per unit of camera distance, as the two size bounds are.
            var startFadeSlope = startFadeSize.NextNumber(systemRenderState);
            var endFadeSlope = endFadeSize.NextNumber(systemRenderState);

            // The shader fades by view angle in every mode, not just the normal-aligned one; the
            // defaults of (1, 2) put the smoothstep past its own range, which is what makes it inert.
            var viewAngleFadeActive = enableFadingAndClamping
                && startFadeDot < 1f
                && endFadeDot > startFadeDot;

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

                    // The engine clamps the full extent of the trail, and it clamps unconditionally: an
                    // effect that authors m_flLengthScale 0 alongside a minimum length is asking for a
                    // fixed streak that does not track speed, so a zero raw length still draws.
                    length = Math.Clamp(length, minLength, maxLength);

                    if (length == 0f)
                    {
                        continue;
                    }

                    var particleRadius = particle.Radius * RadiusScale.NextNumber(ref particle, systemRenderState);

                    // Scales rgb and alpha alike, as the view angle fade below touches alpha only
                    var colorFade = 1f;
                    var alphaFade = 1f;

                    if (enableFadingAndClamping)
                    {
                        var cameraDistance = Vector3.Distance(camera.Location, particle.Position);
                        var fadeStart = startFadeSlope * cameraDistance;
                        var fadeEnd = endFadeSlope * cameraDistance;

                        // The fade reads the raw radius, independently of the size clamp below
                        if (particleRadius > fadeStart)
                        {
                            if (particleRadius >= fadeEnd)
                            {
                                continue;
                            }

                            colorFade = 1f - ((particleRadius - fadeStart) / (fadeEnd - fadeStart));
                        }

                        particleRadius = MathF.Min(MathF.Max(particleRadius, minSize * cameraDistance), maxSize * cameraDistance);

                        if (viewAngleFadeActive)
                        {
                            var toCamera = camera.Location - particle.Position;

                            if (toCamera.LengthSquared() > Epsilon.LengthSquared)
                            {
                                // Only the normal-aligned mode has a normal to face with; the others
                                // substitute the direction the ribbon runs in.
                                var facingAxis = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
                                    ? particle.Normal
                                    : direction;

                                var facing = MathF.Abs(Vector3.Dot(Vector3.Normalize(facingAxis), Vector3.Normalize(toCamera)));
                                alphaFade = 1f - MathUtils.Smoothstep(startFadeDot, endFadeDot, facing);
                            }
                        }
                    }

                    // A short trail is narrowed so it cannot render wider than it is long
                    var radius = MathF.Min(particleRadius, constrainRadiusToLengthRatio * length);

                    // The ribbon always runs along the motion; only the plane it is flattened into
                    // changes with the orientation. The spritecard vertex shader takes the up vector
                    // for that plane from three buckets, and only ALIGN_TO_PARTICLE_NORMAL reads the
                    // particle's normal: it is the sole mode whose instance record carries one.
                    var planeNormal = orientationType switch
                    {
                        ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => particle.Normal,
                        ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => Vector3.UnitZ,
                        _ => position - camera.Location,
                    };

                    var widthAxis = Vector3.Cross(planeNormal, direction);
                    widthAxis = widthAxis.LengthSquared() > Epsilon.LengthSquared
                        ? Vector3.Normalize(widthAxis)
                        : Vector3.Normalize(Vector3.Cross(direction, MathF.Abs(direction.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX));

                    var lengthAxis = direction;
                    // The radius is the half extent across the ribbon, while the length spans it end to end
                    var halfWidth = radius;
                    var halfLength = length * 0.5f;

                    // The engine slides the trail along the motion axis by m_flForwardShift lengths;
                    // direction runs backwards along travel here, so the shift subtracts
                    var center = position + (direction * (length * (0.5f - forwardShift)));

                    var headHalfWidth = halfWidth * headRadiusTaper.NextNumber(ref particle, systemRenderState);
                    var tailHalfWidth = halfWidth * tailRadiusTaper.NextNumber(ref particle, systemRenderState);

                    var uvOffset = Vector2.Zero;
                    var uvScale = new Vector2(finalTextureScaleU, finalTextureScaleV);
                    var uvNextOffset = uvOffset;
                    var uvNextScale = uvScale;
                    var frameBlend = 0f;

                    var spriteSheetData = texture.SpriteSheetData;
                    if (spriteSheetData != null && spriteSheetData.Sequences.Length > 0 && spriteSheetData.Sequences[0].Frames.Length > 0)
                    {
                        var sequence = spriteSheetData.Sequences[particle.SequenceNumber % spriteSheetData.Sequences.Length];
                        var (frame, nextFrame, blend) = GetSheetFrame(ref particle, sequence, animationRate, animationType, animateInFps);
                        frameBlend = blend;

                        // TODO: Support more than one image per frame?
                        var currentImage = sequence.Frames[frame].Images[0];
                        var nextImage = sequence.Frames[nextFrame].Images[0];

                        uvOffset = currentImage.UncroppedMin;
                        uvScale *= currentImage.UncroppedMax - currentImage.UncroppedMin;
                        uvNextOffset = nextImage.UncroppedMin;
                        uvNextScale *= nextImage.UncroppedMax - nextImage.UncroppedMin;
                    }

                    // Corners in index buffer winding order, with the local quad's [-1, 1] axes mapping to [0, 1] uvs
                    var quadStart = quadCount * VertexSize * 4;
                    var alpha = particle.Alpha * particle.AlphaAlternate * colorFade * alphaFade
                        * AlphaScale.NextNumber(ref particle, systemRenderState);
                    var tint = particle.Color * colorFade;

                    var head = Vector4.Clamp(
                        new Vector4(tint * headColor, alpha * headAlphaScale.NextNumber(ref particle, systemRenderState)),
                        Vector4.Zero, Vector4.One);
                    var tail = Vector4.Clamp(
                        new Vector4(tint * tailColor, alpha * tailAlphaScale.NextNumber(ref particle, systemRenderState)),
                        Vector4.Zero, Vector4.One);

                    for (var j = 0; j < 4; ++j)
                    {
                        var corner = QuadCorners[j];
                        var isHead = corner.Y < 0f;
                        var worldPosition = center
                            + (widthAxis * (corner.X * (isHead ? headHalfWidth : tailHalfWidth)))
                            + (lengthAxis * (corner.Y * halfLength));
                        var color = isHead ? head : tail;
                        var cornerUv = (corner * 0.5f) + new Vector2(0.5f);
                        var uv = uvOffset + (cornerUv * uvScale);
                        var uvNext = uvNextOffset + (cornerUv * uvNextScale);

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
                        rawVertices[vertexStart + 9] = uvNext.X;
                        rawVertices[vertexStart + 10] = uvNext.Y;
                        rawVertices[vertexStart + 11] = frameBlend;
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

            GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

            // Trail quads are oriented by motion direction, so either side can face the camera
            GL.Disable(EnableCap.CullFace);

            shader.Use();

            GL.BindVertexArray(vaoHandle);

            shader.SetTexture(RenderMaterial.TextureUnitStart, "uTexture", texture);

            // TODO: This formula is a guess but still seems too bright compared to valve particles
            SetSharedUniforms(shader, systemRenderState);

            shader.SetUniform1("uBlendFrames", blendFrames);

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
