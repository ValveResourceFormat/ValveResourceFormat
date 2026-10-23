using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders the particles of a system as a ribbon threaded through them, textured along its own
    /// arc length in world units.
    /// </summary>
    /// <remarks>
    /// The chain is a Catmull-Rom spline whose control points are consecutive particles, subdivided
    /// per segment by a screen size driven tesselation level. Unlike a cable the ribbon is a flat
    /// strip rather than a tube, and it tiles its texture by world distance rather than stretching
    /// one copy over the whole run.
    ///
    /// <para>The ribbon breaks wherever the <c>m_nSplitField</c> value changes between neighbours,
    /// and each piece splines only through its own particles. With <c>m_bSortBySegmentID</c> the
    /// particles are first grouped by that value, in the order each value first appears.</para>
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderRopes">C_OP_RenderRopes</seealso>
    internal class RenderRopes : ParticleFunctionRenderer
    {
        private const string ShaderName = "particle_spritecard";

        private const string DefaultTextureName = "materials/particle/base_trail.vtex";

        /// <summary>The shared quad index buffer covers 65532 indices, six per quad.</summary>
        private const int MaxQuads = 65532 / 6;
        private const int MaxTessellationLevel = 7;

        /// <summary>
        /// How many segments are written in one batch. The V offset carried over when the first particle
        /// dies grows by the length of the last segment of the first batch.
        /// </summary>
        private const int SegmentBatchSize = 527;

        private readonly Shader shader;
        private readonly RendererContext rendererContext;
        private readonly int vaoHandle;
        private readonly int vertexBufferHandle;

        private readonly ParticleBlendMode blendMode = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;
        private readonly ParticleTextureLayer[] layers;
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
        private readonly ParticleField splitField = ParticleField.RopeSegmentId;
        private readonly bool sortBySegmentId;
        private readonly bool drawAsOpaque;

        private readonly bool enableFadingAndClamping;
        private readonly float minSize;
        private readonly float maxSize = 2000f;
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(1000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(2000f);
        private readonly float startFadeDot = 1f;
        private readonly float endFadeDot = 2f;

        private RopeNode[] nodeScratch = [];
        private long[] sortKeyScratch = [];
        private readonly Dictionary<int, int> segmentRanks = [];

        private readonly Dictionary<int, int> segmentRepeats = [];
        private bool segmentDataDirty;

        private float vOffsetAccumulator;
        private float firstSegmentLength;
        private int firstParticleId = -1;

        private struct RopeNode
        {
            public int ParticleIndex;
            public float Segment;
            public Vector3 Position;
            public float Radius;
            public Vector3 Color;
            public float Alpha;
            public Vector3 Orientation;
            public float ScalarCoordinate;
            public Vector2 AlphaReference;
        }

        private struct RopeSample
        {
            public Vector3 Position { get; init; }
            public Vector3 WidthAxis { get; set; }
            public Vector3 Tangent { get; init; }
            public float Radius { get; init; }
            public Vector4 Color { get; init; }
            public Vector2 AlphaReference { get; init; }
        }

        public RenderRopes(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse, scene)
        {
            this.rendererContext = rendererContext;

            blendMode = parse.Enum("m_nOutputBlendMode", blendMode);

            (layers, var textureName) = ParticleTextureLayer.Build(parse, rendererContext, DefaultTextureName, srgbRead: TexturesAreColor);

            var shaderArguments = CreateShaderArguments();
            shaderArguments["S_TEXTURE_LAYERS"] = (byte)(layers.Length - 1);
            AddSpritecardShaderArguments(shaderArguments);

            shader = rendererContext.ShaderLoader.LoadShader(ShaderName, shaderArguments);
            (vaoHandle, vertexBufferHandle) = SetupQuadBuffer($"{nameof(RenderRopes)}: {System.IO.Path.GetFileName(textureName)}");

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
            splitField = parse.ParticleField("m_nSplitField", splitField);
            sortBySegmentId = parse.Boolean("m_bSortBySegmentID", sortBySegmentId);
            drawAsOpaque = parse.Boolean("m_bDrawAsOpaque", drawAsOpaque);

            enableFadingAndClamping = parse.Boolean("m_bEnableFadingAndClamping", enableFadingAndClamping);
            minSize = parse.Float("m_flMinSize", minSize);
            maxSize = parse.Float("m_flMaxSize", maxSize);
            startFadeSize = parse.NumberProvider("m_flStartFadeSize", startFadeSize);
            endFadeSize = parse.NumberProvider("m_flEndFadeSize", endFadeSize);
            startFadeDot = parse.Float("m_flStartFadeDot", startFadeDot);
            endFadeDot = parse.Float("m_flEndFadeDot", endFadeDot);

            orientationType = (ParticleOrientation)Math.Clamp((int)orientationType,
                (int)ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED, (int)ParticleOrientation.PARTICLE_ORIENTATION_FULL_3AXIS_ROTATION);

            Pass = drawAsOpaque ? RenderPass.Opaque : RenderPass.Translucent;
        }

        /// <inheritdoc/>
        public override Texture.SpritesheetData? SpriteSheet => ParticleTextureLayer.FindSpriteSheet(layers);

        private bool Splits => splitField is >= 0 and not ParticleField.NoneDisabled;

        /// <inheritdoc/>
        public override ParticleEventType ObservedEvents => Splits
            ? ParticleEventType.PARTICLE_EVENT_TYPE_MASK_SPAWNED | ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED
            : ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;

        /// <inheritdoc/>
        public override void OnParticleEvent(ParticleCollection particles, ParticleEventType eventType, ReadOnlySpan<int> indices)
        {
            segmentDataDirty = true;
        }

        /// <inheritdoc/>
        public override void Simulate(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (!segmentDataDirty || GetOperatorRunStrength(systemState) <= 0f)
            {
                return;
            }

            segmentDataDirty = false;

            if (sortBySegmentId)
            {
                UpdateSegmentData(particles.Current);
            }
        }

        /// <summary>
        /// Writes each particle's place in its rope segment to <see cref="Particle.RopeSegmentData"/>:
        /// how many particles of the segment come before it in x, that as a fraction of the segment's
        /// last place in y, and the last place in z. Segments are told apart by the bits of their value.
        /// </summary>
        private void UpdateSegmentData(Span<Particle> particles)
        {
            segmentRepeats.Clear();

            foreach (ref var particle in particles)
            {
                var segmentBits = BitConverter.SingleToInt32Bits(particle.GetScalar(splitField));
                var place = segmentRepeats.TryGetValue(segmentBits, out var repeats) ? repeats + 1 : 0;

                segmentRepeats[segmentBits] = place;
                particle.RopeSegmentData = new Vector3(place, 0f, 0f);
            }

            foreach (ref var particle in particles)
            {
                var last = segmentRepeats[BitConverter.SingleToInt32Bits(particle.GetScalar(splitField))];
                var place = particle.RopeSegmentData.X;

                particle.RopeSegmentData = new Vector3(place, last > 0 ? place / last : 0f, last);
            }
        }

        /// <inheritdoc/>
        public override void SetTextureOverride(RenderTexture texture)
        {
            // The override stands in for the base texture; layers composited over it keep their own.
            layers[0].Texture = texture;
        }

        private (int Vao, int Buffer) SetupQuadBuffer(string label)
        {
            var buffer = GraphicsDevice.CreateBuffer(label);
            var vao = SpritecardVertex.InputLayout.CreateVertexArray(label, buffer, rendererContext.MeshBufferCache.QuadIndices.GLHandle);

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

        /// <summary>
        /// Picks one subdivision level for the whole collection from its projected size. The ceiling
        /// is lowered to the measured value before the floor is applied, so a ceiling above 128 or
        /// below 1 both leave the count pinned rather than following the floor.
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

            var diagonal = Vector3.Distance(max, min);
            var centre = (min + max) * 0.5f;
            var distanceSquared = Vector3.DistanceSquared(centre, camera.Location);

            var coverage = distanceSquared <= diagonal * diagonal
                ? 1f
                : MathUtils.Saturate(diagonal * camera.ProjectionMatrix.M22 / MathF.Sqrt(distanceSquared));

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
        private (float ScrollRate, float Offset, float OneOverWorldSize) ResolveTextureV(ParticleSystemState systemState)
        {
            var scrollRate = textureVScrollRate.NextNumber(systemState);
            var offset = textureVOffset.NextNumber(systemState);
            var worldSize = textureVWorldSize.NextNumber(systemState);

            if (textureVParamsControlPoint >= 0)
            {
                var parameters = systemState.GetControlPoint(textureVParamsControlPoint).Position;

                return (parameters.Y, parameters.Z, 1f / MathF.Max(ParticleMath.FloatEpsilon, parameters.X));
            }

            var modulated = (scaleVSizeByControlPointDistance > 0f
                    || scaleVScrollByControlPointDistance > 0f
                    || scaleVOffsetByControlPointDistance > 0f)
                && scaleControlPoint1 > -1
                && scaleControlPoint2 > -1;

            if (modulated)
            {
                var distance = Vector3.Distance(
                    systemState.GetControlPoint(scaleControlPoint1).Position,
                    systemState.GetControlPoint(scaleControlPoint2).Position);

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

            return (scrollRate, offset, 1f / MathF.Max(ParticleMath.FloatEpsilon, worldSize));
        }

        /// <summary>
        /// Copies the live particles into the node scratch in walk order, applying the size clamp and
        /// the distance fade that <c>m_bEnableFadingAndClamping</c> gates.
        /// </summary>
        /// <remarks>
        /// The tint is truncated to bytes without a clamp, so a channel past 1 wraps around. The alpha
        /// scale applies twice: once with the particle's alpha and again where the colour scale combines.
        /// </remarks>
        private int CollectNodes(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            var count = particleBag.Count;

            if (nodeScratch.Length < count)
            {
                nodeScratch = new RopeNode[count];
            }

            var startFadeSlope = startFadeSize.NextNumber(systemState);
            var endFadeSlope = endFadeSize.NextNumber(systemState);
            var splits = Splits;
            var scales = EvaluateScales(systemState);

            if (scales.Radius == 0f || scales.Alpha == 0f)
            {
                return 0;
            }

            for (var i = 0; i < count; i++)
            {
                var particleIndex = reverseOrder ? count - i - 1 : i;
                ref var particle = ref particleBag.Current[particleIndex];

                var radius = particle.Radius * scales.Radius;
                var color = ParticleColorBlend.Apply(ColorBlendType,
                    ParticleColorBlend.ToWrappedBytes(particle.Color, Math.Clamp(scales.Alpha * 255f * GetAlpha2(ref particle) * particle.Alpha, 0f, 255f)),
                    scales.Color) / 255f;
                var alpha = color.W;
                var alphaReference = GetAlphaReference(ref particle, radius, alpha);

                if (enableFadingAndClamping)
                {
                    var cameraDistance = Vector3.Distance(camera.Location, particle.Position);
                    var fadeStart = startFadeSlope * cameraDistance;
                    var fadeEnd = endFadeSlope * cameraDistance;

                    if (radius > fadeStart && fadeEnd > fadeStart)
                    {
                        alpha *= MathF.Max(0f, 1f - MathUtils.Remap(radius, fadeStart, fadeEnd));
                    }

                    radius = MathF.Min(MathF.Max(radius, minSize * cameraDistance), maxSize * cameraDistance);
                }

                nodeScratch[i] = new RopeNode
                {
                    ParticleIndex = particleIndex,
                    Segment = splits ? particle.GetScalar(splitField) : 0f,
                    Position = particle.Position,
                    Radius = radius,
                    Color = color.AsVector3(),
                    Alpha = alpha,
                    Orientation = particle.GetVector(orientationField),
                    ScalarCoordinate = useScalarForTextureCoordinate
                        ? particle.GetScalar(scalarFieldForTextureCoordinate) * scalarAttributeTextureCoordScale
                        : 0f,
                    AlphaReference = alphaReference,
                };
            }

            if (sortBySegmentId)
            {
                GroupBySegment(nodeScratch.AsSpan(0, count));
            }

            return count;
        }

        /// <summary>
        /// Stably groups the nodes by segment, ordering the groups by where each segment first appears.
        /// Segments are told apart by the bits of their value, so 0 and -0 are separate segments.
        /// </summary>
        private void GroupBySegment(Span<RopeNode> nodes)
        {
            if (sortKeyScratch.Length < nodes.Length)
            {
                sortKeyScratch = new long[nodes.Length];
            }

            var keys = sortKeyScratch.AsSpan(0, nodes.Length);
            segmentRanks.Clear();

            for (var i = 0; i < nodes.Length; i++)
            {
                var segmentBits = BitConverter.SingleToInt32Bits(nodes[i].Segment);

                if (!segmentRanks.TryGetValue(segmentBits, out var rank))
                {
                    rank = segmentRanks.Count;
                    segmentRanks.Add(segmentBits, rank);
                }

                keys[i] = ((long)rank << 32) | (uint)i;
            }

            keys.Sort(nodes);
        }

        /// <summary>
        /// Builds the ribbon into the shared vertex buffer and returns how many quads were written.
        /// V is raw world arc length measured between the particles themselves, so subdividing a
        /// segment keeps the same V span as the straight chord it replaces. A segment joining two
        /// rope segments, or one of no length, is not drawn, but its length still advances V. The
        /// segment of an open rope that ends on the first particle ends at V 0.
        /// </summary>
        private int UpdateVertices(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            var nodeCount = CollectNodes(particleBag, systemState, camera);

            if (nodeCount < 2)
            {
                return 0;
            }

            var nodes = nodeScratch.AsSpan(0, nodeCount);
            var segmentCount = closedLoop ? nodeCount : nodeCount - 1;
            var subdivisions = 1 << ComputeTessellationLevel(nodes, camera);

            var (scrollRate, vOffset, oneOverWorldSize) = ResolveTextureV(systemState);
            CarryVOffset(particleBag, oneOverWorldSize);

            var viewAngleFadeActive = startFadeDot < 1f && endFadeDot > startFadeDot;
            Span<SheetFrame> sheets = stackalloc SheetFrame[ParticleTextureLayer.MaxLayers];
            Span<ParticleTextureLayer.UvTransform> uvTransforms = stackalloc ParticleTextureLayer.UvTransform[ParticleTextureLayer.MaxLayers];

            ParticleTextureLayer.ResolveUvTransforms(layers, systemState, uvTransforms);

            for (var layer = 0; layer < layers.Length; layer++)
            {
                sheets[layer] = ResolveSheetFrame(particleBag, layers[layer].Texture);
            }

            var maxQuads = Math.Min(MaxQuads, segmentCount * subdivisions);
            var quadCount = 0;
            var arcLength = (scrollRate * systemState.Age) + vOffsetAccumulator;

            using (var vertexBuffer = new RentedBuffer<SpritecardVertex>(maxQuads * 4))
            {
                var vertices = vertexBuffer.Span;
                var stripAxis = Vector3.Zero;

                for (var segment = 0; segment < segmentCount && quadCount < maxQuads; segment++)
                {
                    ref readonly var n1 = ref nodes[ResolveIndex(segment, nodeCount)];
                    ref readonly var n2 = ref nodes[ResolveIndex(segment + 1, nodeCount)];

                    var segmentLength = Vector3.Distance(n1.Position, n2.Position);
                    var endsAtFirst = n2.ParticleIndex == 0 && !closedLoop;

                    if (segment < SegmentBatchSize)
                    {
                        firstSegmentLength = segmentLength;
                    }

                    var vStart = arcLength;
                    var vEnd = endsAtFirst ? 0f : arcLength + segmentLength;
                    arcLength += segmentLength;

                    if (n1.Segment != n2.Segment || segmentLength == 0f)
                    {
                        stripAxis = Vector3.Zero;
                        continue;
                    }

                    ref readonly var before = ref nodes[ResolveIndex(segment - 1, nodeCount)];
                    ref readonly var after = ref nodes[ResolveIndex(segment + 2, nodeCount)];
                    ref readonly var n0 = ref before.Segment == n1.Segment ? ref before : ref n1;
                    ref readonly var n3 = ref after.Segment == n2.Segment ? ref after : ref n2;

                    if (useScalarForTextureCoordinate)
                    {
                        vStart = n1.ScalarCoordinate;
                        vEnd = n2.ParticleIndex == 0 ? n1.ScalarCoordinate : n2.ScalarCoordinate;
                    }

                    var previous = EvaluateSample(n0, n1, n2, n3, 0f, camera);
                    AlignWidthAxis(ref previous, ref stripAxis);

                    for (var step = 1; step <= subdivisions && quadCount < maxQuads; step++)
                    {
                        var t = step / (float)subdivisions;
                        var current = EvaluateSample(n0, n1, n2, n3, t, camera);
                        AlignWidthAxis(ref current, ref stripAxis);

                        WriteQuad(vertices, quadCount, previous, current,
                            MapV(MathUtils.Lerp(vStart, vEnd, (step - 1) / (float)subdivisions), oneOverWorldSize, vOffset),
                            MapV(MathUtils.Lerp(vStart, vEnd, t), oneOverWorldSize, vOffset),
                            sheets, uvTransforms, viewAngleFadeActive, camera);

                        quadCount++;
                        previous = current;
                    }
                }

                if (quadCount > 0)
                {
                    GL.NamedBufferData(vertexBufferHandle, quadCount * 4 * SpritecardVertex.InputLayout.Stride, vertexBuffer.ByteArray, BufferUsageHint.DynamicDraw);
                }
            }

            return quadCount;
        }

        /// <summary>
        /// Adds <see cref="firstSegmentLength"/> to the carried V offset, wrapped to the texture's world size,
        /// whenever a different particle is first, as when the rope's oldest particle dies.
        /// </summary>
        private void CarryVOffset(ParticleCollection particleBag, float oneOverWorldSize)
        {
            var particleId = particleBag.Current[0].ParticleId;

            if (firstParticleId != -1 && particleId != firstParticleId)
            {
                vOffsetAccumulator = (vOffsetAccumulator + firstSegmentLength) % (1f / oneOverWorldSize);
            }

            firstParticleId = particleId;
        }

        private float MapV(float coordinate, float oneOverWorldSize, float vOffset)
        {
            var v = (coordinate * oneOverWorldSize) + vOffset;

            return clampV ? MathUtils.Saturate(v) : v;
        }

        /// <summary>
        /// Splines position, radius and colour between the two middle control points and builds the
        /// ribbon's width axis from the spline tangent and the plane the orientation type selects.
        /// </summary>
        private RopeSample EvaluateSample(in RopeNode n0, in RopeNode n1, in RopeNode n2, in RopeNode n3, float t, Camera camera)
        {
            var position = CableMeshBuilder.CatmullRom(n0.Position, n1.Position, n2.Position, n3.Position, t);
            var radius = CableMeshBuilder.CatmullRom(n0.Radius, n1.Radius, n2.Radius, n3.Radius, t);

            var tangent = CableMeshBuilder.CatmullRomTangent(n0.Position, n1.Position, n2.Position, n3.Position, t);
            tangent = MathUtils.SafeNormalize(tangent, Vector3.UnitX, ParticleMath.MinimumLengthSquared);

            var planeNormal = orientationType switch
            {
                ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => Vector3.Lerp(n1.Orientation, n2.Orientation, t),
                ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => Vector3.UnitZ,
                _ => camera.Location - position,
            };

            var widthAxis = Vector3.Cross(tangent, planeNormal);
            widthAxis = widthAxis.LengthSquared() > ParticleMath.MinimumLengthSquared
                ? Vector3.Normalize(widthAxis)
                : Vector3.Normalize(Vector3.Cross(tangent, MathF.Abs(tangent.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX));

            return new RopeSample
            {
                Position = position,
                WidthAxis = widthAxis,
                Tangent = tangent,
                Radius = radius,
                Color = Vector4.Clamp(
                    new Vector4(Vector3.Lerp(n1.Color, n2.Color, t), MathUtils.Lerp(n1.Alpha, n2.Alpha, t)),
                    Vector4.Zero, Vector4.One),
                AlphaReference = n1.AlphaReference,
            };
        }

        private void WriteQuad(Span<SpritecardVertex> vertices, int quadIndex, in RopeSample start, in RopeSample end,
            float vStart, float vEnd, ReadOnlySpan<SheetFrame> sheets, ReadOnlySpan<ParticleTextureLayer.UvTransform> uvTransforms,
            bool viewAngleFadeActive, Camera camera)
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

            var quadStart = quadIndex * 4;

            for (var j = 0; j < 4; j++)
            {
                var atStart = j is 0 or 3;
                ref var vertex = ref vertices[quadStart + j];
                vertex = default;
                vertex.Position = positions[j];
                vertex.Color = atStart ? startColor : endColor;
                vertex.FrameBlendAlphaReference = new Vector3(sheets[0].Blend, start.AlphaReference.X, start.AlphaReference.Y);
                vertex.UV = ParticleTextureLayer.PlaceCorner(coordinates[j], uvTransforms[0], sheets[0].Offset, sheets[0].Offset + sheets[0].Scale);
                vertex.UVNextFrame = ParticleTextureLayer.PlaceCorner(coordinates[j], uvTransforms[0], sheets[0].NextOffset, sheets[0].NextOffset + sheets[0].NextScale);

                for (var layer = 1; layer < layers.Length; layer++)
                {
                    ref readonly var sheet = ref sheets[layer];
                    var layerUv = ParticleTextureLayer.PlaceCorner(coordinates[j], uvTransforms[layer], sheet.Offset, sheet.Offset + sheet.Scale);
                    var layerUvNext = ParticleTextureLayer.PlaceCorner(coordinates[j], uvTransforms[layer], sheet.NextOffset, sheet.NextOffset + sheet.NextScale);
                    vertex.SetLayerUv(layer - 1, new Vector4(layerUv.X, layerUv.Y, layerUvNext.X, layerUvNext.Y));
                }
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

            if (toCamera.LengthSquared() <= ParticleMath.MinimumLengthSquared)
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
        private SheetFrame ResolveSheetFrame(ParticleCollection particleBag, RenderTexture layerTexture)
        {
            var identity = new SheetFrame { Offset = Vector2.Zero, Scale = Vector2.One, NextOffset = Vector2.Zero, NextScale = Vector2.One };

            var spriteSheetData = layerTexture.SpriteSheetData;

            if (spriteSheetData == null || spriteSheetData.Sequences.Length == 0)
            {
                return identity;
            }

            ref var head = ref particleBag.Current[0];
            var sequenceNumber = (int)head.SequenceNumber;
            var sequence = spriteSheetData.Sequences[(uint)sequenceNumber < (uint)spriteSheetData.Sequences.Length ? sequenceNumber : 0];

            if (sequence.Frames.Length == 0)
            {
                return identity;
            }
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

        private int quadCount;

        /// <inheritdoc/>
        public override void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            quadCount = particles.Count == 0 ? 0 : UpdateVertices(particles, systemState, camera);
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            if (particleBag.Count < 2)
            {
                return;
            }

            if (quadCount == 0)
            {
                return;
            }

            using var _ = SpritecardStateScope(blendMode, drawAsOpaque);

            shader.Use();
            VertexArray.Bind(vaoHandle, shader);

            ParticleTextureLayer.Bind(shader, layers, systemState);
            SetSharedUniforms(shader, systemState);
            shader.SetUniform1("uBlendFrames", blendFrames);

            // Set every draw: the program is shared with every other spritecard renderer, whatever their mode.
            shader.SetUniform1("uBlendMode", (int)blendMode);
            shader.SetUniform1("uOutline", false);

            PerfStats.Active.Count(Counter.ParticleDraw);
            GL.DrawElements(PrimitiveType.Triangles, quadCount * 6, DrawElementsType.UnsignedShort, 0);
        }

        /// <inheritdoc/>
        public override bool CanRenderReplacement => true;

        /// <inheritdoc/>
        public override void RenderReplacement(Shader replacement, uint objectId)
            => DrawReplacement(replacement, objectId, vaoHandle, quadCount * 6, DrawElementsType.UnsignedShort);

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        /// <inheritdoc/>
        public override void SetRenderMode(string renderMode)
        {
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            VertexArray.Delete(vaoHandle);
            GL.DeleteBuffer(vertexBufferHandle);
        }
    }
}
