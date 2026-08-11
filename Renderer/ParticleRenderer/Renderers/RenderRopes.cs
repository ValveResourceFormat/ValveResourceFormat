using System.Buffers;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders the particles of a system as one continuous ribbon threaded through them, textured
    /// along its own arc length in world units.
    /// </summary>
    /// <remarks>
    /// The chain is a Catmull-Rom spline whose control points are consecutive particles, subdivided
    /// per segment by a screen size driven tesselation level. Unlike a cable the ribbon is a flat
    /// strip rather than a tube, and it tiles its texture by world distance rather than stretching
    /// one copy over the whole run.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderRopes">C_OP_RenderRopes</seealso>
    internal class RenderRopes : ParticleFunctionRenderer
    {
        private const string ShaderName = "particle_trail";

        /// <summary>Floats per vertex: position 3, colour 4, uv 2, next frame uv 2, frame blend 1.</summary>
        private const int VertexSize = 12;
        private const string DefaultTextureName = "materials/particle/base_trail.vtex";

        /// <summary>The shared quad index buffer covers 65532 indices, six per quad.</summary>
        private const int MaxQuads = 65532 / 6;
        private const int MaxTessellationLevel = 7;

        private readonly Shader shader;
        private readonly RendererContext rendererContext;
        private readonly int vaoHandle;
        private readonly int vertexBufferHandle;
        private RenderTexture texture;

        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly ParticleOrientation orientationType = ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED;
        private readonly ParticleField orientationField = ParticleField.Normal;

        private readonly float animationRate = 0.1f;
        private readonly ParticleAnimationType animationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
        private readonly bool animateInFps;
        private readonly bool blendFrames = true;

        private readonly int minTessellation = 1;
        private readonly int maxTessellation = 128;
        private readonly float tessScale = 1f;

        private readonly INumberProvider textureVWorldSize = new LiteralNumberProvider(10f);
        private readonly INumberProvider textureVScrollRate = new LiteralNumberProvider(0f);
        private readonly INumberProvider textureVOffset = new LiteralNumberProvider(0f);
        private readonly int textureVParamsControlPoint = -1;
        private readonly bool clampV;

        private readonly int scaleControlPoint1 = -1;
        private readonly int scaleControlPoint2 = -1;
        private readonly float scaleVSizeByControlPointDistance;
        private readonly float scaleVScrollByControlPointDistance;
        private readonly float scaleVOffsetByControlPointDistance;

        private readonly bool useScalarForTextureCoordinate;
        private readonly ParticleField scalarFieldForTextureCoordinate = ParticleField.CreationTime;
        private readonly float scalarAttributeTextureCoordScale = 1f;

        private readonly bool reverseOrder;
        private readonly bool closedLoop;
        private readonly bool drawAsOpaque;

        private readonly bool enableFadingAndClamping;
        private readonly float minSize;
        private readonly float maxSize = 2000f;
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(1000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(2000f);
        private readonly float startFadeDot = 1f;
        private readonly float endFadeDot = 2f;

        private RopeNode[] nodeScratch = [];

        private struct RopeNode
        {
            public Vector3 Position;
            public float Radius;
            public Vector3 Color;
            public float Alpha;
            public Vector3 Orientation;
            public float ScalarCoordinate;
        }

        private struct RopeSample
        {
            public Vector3 Position { get; init; }
            public Vector3 WidthAxis { get; set; }
            public Vector3 Tangent { get; init; }
            public float Radius { get; init; }
            public Vector4 Color { get; init; }
        }

        public RenderRopes(ParticleDefinitionParser parse, RendererContext rendererContext) : base(parse)
        {
            this.rendererContext = rendererContext;

            blendMode = parse.Enum("m_nOutputBlendMode", blendMode);
            shader = rendererContext.ShaderLoader.LoadShader(ShaderName);
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
                    textureName = textures[0].Data.GetStringProperty("m_hTexture");
                }
            }

            texture = rendererContext.MaterialLoader.GetTexture(textureName ?? DefaultTextureName, srgbRead: true);

#if DEBUG
            var vaoLabel = $"{nameof(RenderRopes)}: {System.IO.Path.GetFileName(textureName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, vertexBufferHandle, Math.Min(GLEnvironment.MaxLabelLength, vaoLabel.Length), vaoLabel);
#endif

            orientationType = parse.Enum("m_nOrientationType", orientationType);
            orientationField = parse.ParticleField("m_nVectorFieldForOrientation", orientationField);

            animationRate = parse.Float("m_flAnimationRate", animationRate);
            animationType = parse.Enum("m_nAnimationType", animationType);
            animateInFps = parse.Boolean("m_bAnimateInFPS", animateInFps);
            blendFrames = parse.Boolean("m_bBlendFramesSeq0", blendFrames);

            minTessellation = parse.Int32("m_nMinTesselation", minTessellation);
            maxTessellation = parse.Int32("m_nMaxTesselation", maxTessellation);
            tessScale = parse.Float("m_flTessScale", tessScale);

            textureVWorldSize = parse.NumberProvider("m_flTextureVWorldSize", textureVWorldSize);
            textureVScrollRate = parse.NumberProvider("m_flTextureVScrollRate", textureVScrollRate);
            textureVOffset = parse.NumberProvider("m_flTextureVOffset", textureVOffset);
            textureVParamsControlPoint = parse.Int32("m_nTextureVParamsCP", textureVParamsControlPoint);
            clampV = parse.Boolean("m_bClampV", clampV);

            scaleControlPoint1 = parse.Int32("m_nScaleCP1", scaleControlPoint1);
            scaleControlPoint2 = parse.Int32("m_nScaleCP2", scaleControlPoint2);
            scaleVSizeByControlPointDistance = parse.Float("m_flScaleVSizeByControlPointDistance", scaleVSizeByControlPointDistance);
            scaleVScrollByControlPointDistance = parse.Float("m_flScaleVScrollByControlPointDistance", scaleVScrollByControlPointDistance);
            scaleVOffsetByControlPointDistance = parse.Float("m_flScaleVOffsetByControlPointDistance", scaleVOffsetByControlPointDistance);

            useScalarForTextureCoordinate = parse.Boolean("m_bUseScalarForTextureCoordinate", useScalarForTextureCoordinate);
            scalarFieldForTextureCoordinate = parse.ParticleField("m_nScalarFieldForTextureCoordinate", scalarFieldForTextureCoordinate);
            scalarAttributeTextureCoordScale = parse.Float("m_flScalarAttributeTextureCoordScale", scalarAttributeTextureCoordScale);

            reverseOrder = parse.Boolean("m_bReverseOrder", reverseOrder);
            closedLoop = parse.Boolean("m_bClosedLoop", closedLoop);
            drawAsOpaque = parse.Boolean("m_bDrawAsOpaque", drawAsOpaque);

            enableFadingAndClamping = parse.Boolean("m_bEnableFadingAndClamping", enableFadingAndClamping);
            minSize = parse.Float("m_flMinSize", minSize);
            maxSize = parse.Float("m_flMaxSize", maxSize);
            startFadeSize = parse.NumberProvider("m_flStartFadeSize", startFadeSize);
            endFadeSize = parse.NumberProvider("m_flEndFadeSize", endFadeSize);
            startFadeDot = parse.Float("m_flStartFadeDot", startFadeDot);
            endFadeDot = parse.Float("m_flEndFadeDot", endFadeDot);

            // Anything outside 0..4 becomes 5, which the engine's own shader folds back onto 4.
            if ((uint)orientationType > (uint)ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL)
            {
                orientationType = ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL;
            }

            Pass = drawAsOpaque ? RenderPass.Opaque : RenderPass.Translucent;
        }

        /// <inheritdoc/>
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
        /// Resolves a spline control point index against the chain. A closed loop wraps so the seam
        /// stays smooth; an open rope clamps so a segment's tangents never reach past either end.
        /// </summary>
        private int ResolveIndex(int index, int count)
        {
            if (closedLoop)
            {
                if (index < 0)
                {
                    return count - 1;
                }

                return index >= count ? index - count : index;
            }

            return Math.Clamp(index, 0, count - 1);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;

            return 0.5f * ((2f * p1)
                + ((-p0 + p2) * t)
                + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
                + ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
        }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;

            return 0.5f * ((2f * p1)
                + ((-p0 + p2) * t)
                + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
                + ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
        }

        private static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;

            return 0.5f * ((-p0 + p2)
                + (((4f * p0) - (10f * p1) + (8f * p2) - (2f * p3)) * t)
                + (((-3f * p0) + (9f * p1) - (9f * p2) + (3f * p3)) * t2));
        }

        /// <summary>
        /// Picks one subdivision level for the whole collection from its projected size, as the engine
        /// does. The ceiling is lowered to the measured value before the floor is applied, so a
        /// ceiling above 128 or below 1 both leave the count pinned rather than following the floor.
        /// </summary>
        private int ComputeTessellationLevel(ReadOnlySpan<RopeNode> nodes, Camera camera)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (ref readonly var node in nodes)
            {
                min = Vector3.Min(min, node.Position);
                max = Vector3.Max(max, node.Position);
            }

            var diagonal = (max - min).Length();
            var centre = (min + max) * 0.5f;
            var distanceSquared = (centre - camera.Location).LengthSquared();

            var coverage = distanceSquared <= diagonal * diagonal
                ? 1f
                : Math.Clamp(diagonal * camera.ProjectionMatrix.M22 / MathF.Sqrt(distanceSquared), 0f, 1f);

            var metric = MathF.Sqrt(coverage) * 1024f / nodes.Length;
            var raw = (int)(metric * tessScale * 0.1f);

            var subdivisions = maxTessellation >= raw ? raw : maxTessellation;

            if (minTessellation > subdivisions)
            {
                subdivisions = minTessellation;
            }

            if (subdivisions is > 128 or < 1)
            {
                subdivisions = subdivisions > 128 ? 128 : 1;
            }

            var level = 0;

            while (level < MaxTessellationLevel && 1 << level < subdivisions)
            {
                level++;
            }

            return level;
        }

        /// <summary>
        /// Evaluates the three V inputs once for the collection. A control point named by
        /// <c>m_nTextureVParamsCP</c> replaces all three outright; otherwise the control point
        /// distance factors add to them in world units. The two are mutually exclusive.
        /// </summary>
        private (float ScrollRate, float Offset, float OneOverWorldSize) ResolveTextureV(ParticleSystemRenderState systemRenderState)
        {
            var scrollRate = textureVScrollRate.NextNumber(systemRenderState);
            var offset = textureVOffset.NextNumber(systemRenderState);
            var worldSize = textureVWorldSize.NextNumber(systemRenderState);

            if (textureVParamsControlPoint >= 0)
            {
                var parameters = systemRenderState.GetControlPoint(textureVParamsControlPoint).Position;

                return (parameters.Y, parameters.Z, 1f / MathF.Max(float.Epsilon, parameters.X));
            }

            var modulated = (scaleVSizeByControlPointDistance > 0f
                    || scaleVScrollByControlPointDistance > 0f
                    || scaleVOffsetByControlPointDistance > 0f)
                && scaleControlPoint1 > -1
                && scaleControlPoint2 > -1;

            if (modulated)
            {
                var distance = Vector3.Distance(
                    systemRenderState.GetControlPoint(scaleControlPoint1).Position,
                    systemRenderState.GetControlPoint(scaleControlPoint2).Position);

                if (scaleVSizeByControlPointDistance > 0f)
                {
                    worldSize = (scaleVSizeByControlPointDistance * distance) + worldSize;
                }

                if (scaleVScrollByControlPointDistance > 0f)
                {
                    scrollRate = (scaleVScrollByControlPointDistance * distance) + scrollRate;
                }

                if (scaleVOffsetByControlPointDistance > 0f)
                {
                    offset = (scaleVOffsetByControlPointDistance * distance) + offset;
                }
            }

            return (scrollRate, offset, 1f / MathF.Max(float.Epsilon, worldSize));
        }

        /// <summary>
        /// Copies the live particles into the node scratch in walk order, applying the size clamp and
        /// the distance fade that <c>m_bEnableFadingAndClamping</c> gates.
        /// </summary>
        private int CollectNodes(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            var count = particleBag.Count;

            if (nodeScratch.Length < count)
            {
                nodeScratch = new RopeNode[count];
            }

            var startFadeSlope = startFadeSize.NextNumber(systemRenderState);
            var endFadeSlope = endFadeSize.NextNumber(systemRenderState);

            for (var i = 0; i < count; i++)
            {
                ref var particle = ref particleBag.Current[reverseOrder ? count - i - 1 : i];

                var radius = particle.Radius * RadiusScale.NextNumber(ref particle, systemRenderState);
                var alpha = particle.Alpha * particle.AlphaAlternate * AlphaScale.NextNumber(ref particle, systemRenderState);

                if (enableFadingAndClamping)
                {
                    var cameraDistance = Vector3.Distance(camera.Location, particle.Position);
                    var fadeStart = startFadeSlope * cameraDistance;
                    var fadeEnd = endFadeSlope * cameraDistance;

                    if (radius > fadeStart && fadeEnd > fadeStart)
                    {
                        alpha *= MathF.Max(0f, 1f - ((radius - fadeStart) / (fadeEnd - fadeStart)));
                    }

                    radius = MathF.Min(MathF.Max(radius, minSize * cameraDistance), maxSize * cameraDistance);
                }

                nodeScratch[i] = new RopeNode
                {
                    Position = particle.Position,
                    Radius = radius,
                    Color = particle.Color,
                    Alpha = alpha,
                    Orientation = particle.GetVector(orientationField),
                    ScalarCoordinate = useScalarForTextureCoordinate
                        ? particle.GetScalar(scalarFieldForTextureCoordinate) * scalarAttributeTextureCoordScale
                        : 0f,
                };
            }

            return count;
        }

        /// <summary>
        /// Builds the ribbon into the shared vertex buffer and returns how many quads were written.
        /// V is raw world arc length measured between the particles themselves, so subdividing a
        /// segment keeps the same V span as the straight chord it replaces.
        /// </summary>
        private int UpdateVertices(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            var nodeCount = CollectNodes(particleBag, systemRenderState, camera);

            if (nodeCount < 2)
            {
                return 0;
            }

            var nodes = nodeScratch.AsSpan(0, nodeCount);
            var segmentCount = closedLoop ? nodeCount : nodeCount - 1;
            var subdivisions = 1 << ComputeTessellationLevel(nodes, camera);

            var (scrollRate, vOffset, oneOverWorldSize) = ResolveTextureV(systemRenderState);
            var viewAngleFadeActive = startFadeDot < 1f && endFadeDot > startFadeDot;
            var sheet = ResolveSheetFrame(particleBag);

            var maxQuads = Math.Min(MaxQuads, segmentCount * subdivisions);
            var rawVertices = ArrayPool<float>.Shared.Rent(maxQuads * VertexSize * 4);
            var quadCount = 0;
            var arcLength = scrollRate * systemRenderState.Age;

            try
            {
                var stripAxis = Vector3.Zero;

                for (var segment = 0; segment < segmentCount && quadCount < maxQuads; segment++)
                {
                    ref readonly var n0 = ref nodes[ResolveIndex(segment - 1, nodeCount)];
                    ref readonly var n1 = ref nodes[ResolveIndex(segment, nodeCount)];
                    ref readonly var n2 = ref nodes[ResolveIndex(segment + 1, nodeCount)];
                    ref readonly var n3 = ref nodes[ResolveIndex(segment + 2, nodeCount)];

                    var vStart = arcLength;
                    var vEnd = arcLength + Vector3.Distance(n1.Position, n2.Position);
                    arcLength = vEnd;

                    if (useScalarForTextureCoordinate)
                    {
                        vStart = n1.ScalarCoordinate;
                        vEnd = n2.ScalarCoordinate;
                    }

                    var previous = EvaluateSample(n0, n1, n2, n3, 0f, camera);
                    AlignWidthAxis(ref previous, ref stripAxis);

                    for (var step = 1; step <= subdivisions && quadCount < maxQuads; step++)
                    {
                        var t = step / (float)subdivisions;
                        var current = EvaluateSample(n0, n1, n2, n3, t, camera);
                        AlignWidthAxis(ref current, ref stripAxis);

                        WriteQuad(rawVertices, quadCount, previous, current,
                            MapV(float.Lerp(vStart, vEnd, (step - 1) / (float)subdivisions), oneOverWorldSize, vOffset),
                            MapV(float.Lerp(vStart, vEnd, t), oneOverWorldSize, vOffset),
                            sheet, viewAngleFadeActive, camera);

                        quadCount++;
                        previous = current;
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

        private float MapV(float coordinate, float oneOverWorldSize, float vOffset)
        {
            var v = useScalarForTextureCoordinate ? coordinate : (coordinate * oneOverWorldSize) + vOffset;

            return clampV ? Math.Clamp(v, 0f, 1f) : v;
        }

        /// <summary>
        /// Splines position, radius and colour between the two middle control points and builds the
        /// ribbon's width axis from the spline tangent and the plane the orientation type selects.
        /// </summary>
        private RopeSample EvaluateSample(in RopeNode n0, in RopeNode n1, in RopeNode n2, in RopeNode n3, float t, Camera camera)
        {
            var position = CatmullRom(n0.Position, n1.Position, n2.Position, n3.Position, t);
            var radius = CatmullRom(n0.Radius, n1.Radius, n2.Radius, n3.Radius, t);

            var tangent = CatmullRomTangent(n0.Position, n1.Position, n2.Position, n3.Position, t);
            tangent = tangent.LengthSquared() > Epsilon.LengthSquared ? Vector3.Normalize(tangent) : Vector3.UnitX;

            var planeNormal = orientationType switch
            {
                ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => Vector3.Lerp(n1.Orientation, n2.Orientation, t),
                ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => Vector3.UnitZ,
                _ => camera.Location - position,
            };

            var widthAxis = Vector3.Cross(tangent, planeNormal);
            widthAxis = widthAxis.LengthSquared() > Epsilon.LengthSquared
                ? Vector3.Normalize(widthAxis)
                : Vector3.Normalize(Vector3.Cross(tangent, MathF.Abs(tangent.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX));

            return new RopeSample
            {
                Position = position,
                WidthAxis = widthAxis,
                Tangent = tangent,
                Radius = radius,
                Color = Vector4.Clamp(
                    new Vector4(Vector3.Lerp(n1.Color, n2.Color, t), float.Lerp(n1.Alpha, n2.Alpha, t)),
                    Vector4.Zero, Vector4.One),
            };
        }

        private void WriteQuad(float[] rawVertices, int quadIndex, in RopeSample start, in RopeSample end,
            float vStart, float vEnd, in SheetFrame sheet, bool viewAngleFadeActive, Camera camera)
        {
            var startColor = start.Color;
            var endColor = end.Color;

            if (viewAngleFadeActive)
            {
                startColor.W *= ViewAngleFade(start, camera);
                endColor.W *= ViewAngleFade(end, camera);
            }

            Span<Vector3> positions =
            [
                start.Position - (start.WidthAxis * start.Radius),
                end.Position - (end.WidthAxis * end.Radius),
                end.Position + (end.WidthAxis * end.Radius),
                start.Position + (start.WidthAxis * start.Radius),
            ];

            Span<Vector2> coordinates =
            [
                new Vector2(0f, vStart),
                new Vector2(0f, vEnd),
                new Vector2(1f, vEnd),
                new Vector2(1f, vStart),
            ];

            var quadStart = quadIndex * VertexSize * 4;

            for (var j = 0; j < 4; j++)
            {
                var color = j is 0 or 3 ? startColor : endColor;
                var uv = sheet.Offset + (coordinates[j] * sheet.Scale);
                var uvNext = sheet.NextOffset + (coordinates[j] * sheet.NextScale);

                var vertexStart = quadStart + (VertexSize * j);
                rawVertices[vertexStart + 0] = positions[j].X;
                rawVertices[vertexStart + 1] = positions[j].Y;
                rawVertices[vertexStart + 2] = positions[j].Z;
                rawVertices[vertexStart + 3] = color.X;
                rawVertices[vertexStart + 4] = color.Y;
                rawVertices[vertexStart + 5] = color.Z;
                rawVertices[vertexStart + 6] = color.W;
                rawVertices[vertexStart + 7] = uv.X;
                rawVertices[vertexStart + 8] = uv.Y;
                rawVertices[vertexStart + 9] = uvNext.X;
                rawVertices[vertexStart + 10] = uvNext.Y;
                rawVertices[vertexStart + 11] = sheet.Blend;
            }
        }

        /// <summary>
        /// Keeps the ribbon side vector on one hemisphere along the strip. The sign of the width axis
        /// is visually symmetric per sample, but letting it flip between neighboring samples folds
        /// their shared quad into a bowtie where the tangent passes through the view direction.
        /// </summary>
        private static void AlignWidthAxis(ref RopeSample sample, ref Vector3 stripAxis)
        {
            if (Vector3.Dot(sample.WidthAxis, stripAxis) < 0f)
            {
                sample.WidthAxis = -sample.WidthAxis;
            }

            stripAxis = sample.WidthAxis;
        }

        private float ViewAngleFade(in RopeSample sample, Camera camera)
        {
            var toCamera = camera.Location - sample.Position;

            if (toCamera.LengthSquared() <= Epsilon.LengthSquared)
            {
                return 1f;
            }

            var facing = MathF.Abs(Vector3.Dot(sample.Tangent, Vector3.Normalize(toCamera)));

            return 1f - MathUtils.Smoothstep(startFadeDot, endFadeDot, facing);
        }

        private readonly struct SheetFrame
        {
            public Vector2 Offset { get; init; }
            public Vector2 Scale { get; init; }
            public Vector2 NextOffset { get; init; }
            public Vector2 NextScale { get; init; }
            public float Blend { get; init; }
        }

        /// <summary>
        /// Picks the sheet frame for the whole ribbon. Every node is its own particle with its own
        /// age, but the ribbon carries one texture, so it takes the head node's frame.
        /// </summary>
        private SheetFrame ResolveSheetFrame(ParticleCollection particleBag)
        {
            var identity = new SheetFrame { Offset = Vector2.Zero, Scale = Vector2.One, NextOffset = Vector2.Zero, NextScale = Vector2.One };

            var spriteSheetData = texture.SpriteSheetData;

            if (spriteSheetData == null || spriteSheetData.Sequences.Length == 0 || spriteSheetData.Sequences[0].Frames.Length == 0)
            {
                return identity;
            }

            ref var head = ref particleBag.Current[0];
            var sequence = spriteSheetData.Sequences[head.SequenceNumber % spriteSheetData.Sequences.Length];
            var (frame, nextFrame, blend) = GetSheetFrame(ref head, sequence, animationRate, animationType, animateInFps);

            var currentImage = sequence.Frames[frame].Images[0];
            var nextImage = sequence.Frames[nextFrame].Images[0];

            return new SheetFrame
            {
                Offset = currentImage.UncroppedMin,
                Scale = currentImage.UncroppedMax - currentImage.UncroppedMin,
                NextOffset = nextImage.UncroppedMin,
                NextScale = nextImage.UncroppedMax - nextImage.UncroppedMin,
                Blend = blend,
            };
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particleBag, ParticleSystemRenderState systemRenderState, Camera camera)
        {
            if (particleBag.Count < 2)
            {
                return;
            }

            var quadCount = UpdateVertices(particleBag, systemRenderState, camera);

            if (quadCount == 0)
            {
                return;
            }

            if (!drawAsOpaque)
            {
                GL.Enable(EnableCap.Blend);
                GL.DepthMask(false);

                if (blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X)
                {
                    GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
                }
                else
                {
                    GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                }
            }

            GL.Disable(EnableCap.CullFace);

            shader.Use();
            GL.BindVertexArray(vaoHandle);

            shader.SetTexture(RenderMaterial.TextureUnitStart, "uTexture", texture);
            SetSharedUniforms(shader, systemRenderState);
            shader.SetUniform1("uBlendFrames", blendFrames);
            shader.SetUniform1("uBlendMode", (int)blendMode);

            PerfStats.Active.Count(Counter.ParticleDraw);
            GL.DrawElements(PrimitiveType.Triangles, quadCount * 6, DrawElementsType.UnsignedShort, 0);

            GL.Enable(EnableCap.CullFace);

            if (!drawAsOpaque)
            {
                GL.DepthMask(true);
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        /// <inheritdoc/>
        public override void SetRenderMode(string renderMode)
        {
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            GL.DeleteVertexArray(vaoHandle);
            GL.DeleteBuffer(vertexBufferHandle);
        }
    }
}
