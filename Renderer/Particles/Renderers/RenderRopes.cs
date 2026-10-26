using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;

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
    internal class RenderRopes : ParticleSpritecardRenderer
    {
        private const string DefaultTextureName = "materials/particle/base_trail.vtex";

        /// <summary>The shared quad index buffer covers 65532 indices, six per quad.</summary>
        private const int MaxQuads = 65532 / 6;
        private const int MaxSubdivisions = 128;

        /// <summary>
        /// How many segments are written in one batch. The V offset carried over when the first particle
        /// dies grows by the length of the last segment of the first batch.
        /// </summary>
        private const int SegmentBatchSize = 527;

        private readonly ParticleOrientation orientationType = ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED;
        private readonly ParticleField orientationField = ParticleField.Normal;

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

        /// <summary>Whether the visibility inputs scale the rope, which they do from behavior version 11.</summary>
        private readonly bool appliesVisibility;

        private readonly Dictionary<int, int> segmentRepeats = [];
        private readonly Dictionary<int, int> segmentStarts = [];
        private bool segmentDataDirty;

        /// <summary>
        /// The particle indices grouped by segment, as <see cref="UpdateSegmentData"/> last built them.
        /// Only the first <see cref="segmentOrderCount"/> entries are valid.
        /// </summary>
        private int[] segmentOrder = [];
        private int segmentOrderCount;

        /// <summary>The V offset and one over the texture's world size, from the last <see cref="UpdateVertices"/>.</summary>
        private Vector2 vMapping = new(0f, 1f);

        private float vOffsetAccumulator;
        private float firstSegmentLength;
        private int firstParticleId = -1;

        private struct RopeNode
        {
            public int ParticleIndex;

            /// <summary>The bits of the split field, which segments are told apart by.</summary>
            public int SegmentBits;

            public Vector3 Position;
            public float Radius;
            public Vector3 Color;
            public float Alpha;
            public Vector3 Orientation;
            public float ScalarCoordinate;
            public float ExtraData1;
            public float ExtraData2;

            /// <summary>Roll, yaw and pitch, without the roll scale.</summary>
            public Vector3 Rotation;
        }

        private struct RibbonSample
        {
            public Vector3 Position { get; init; }
            public Vector3 WidthAxis { get; set; }

            /// <summary>The axis whose alignment with the view fades the ribbon out by view angle.</summary>
            public Vector3 FacingAxis { get; init; }

            /// <summary>The radius before the size clamp.</summary>
            public float Radius { get; init; }

            /// <summary>The colour, its alpha before any fade.</summary>
            public Vector4 Color { get; init; }
        }

        public RenderRopes(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene)
            : base(parse, rendererContext, scene, DefaultTextureName, instancing: 0)
        {
            CreateVertexArray(SpritecardVertex.InputLayout, rendererContext.MeshBufferCache.QuadIndices.GLHandle);

            orientationType = parse.Enum("m_nOrientationType", orientationType);
            orientationField = parse.ParticleField("m_nVectorFieldForOrientation", orientationField);

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
            appliesVisibility = parse.BehaviorVersion >= 11;

            orientationType = (ParticleOrientation)Math.Clamp((int)orientationType,
                (int)ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED, (int)ParticleOrientation.PARTICLE_ORIENTATION_FULL_3AXIS_ROTATION);

            Pass = drawAsOpaque ? RenderPass.Opaque : RenderPass.Translucent;
        }

        private bool Splits => splitField is >= 0 and not ParticleField.NoneDisabled;

        /// <inheritdoc/>
        public override ulong StoredFields => Splits ? ParticleField.RopeSegmentData.Mask() : 0;

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
        /// Writes each particle's place in its rope segment to <see cref="ParticleField.RopeSegmentData"/>:
        /// how many particles of the segment come before it in x, that as a fraction of the segment's
        /// last place in y, and the last place in z. Segments are told apart by the bits of their value.
        /// Also groups the particles by segment into <see cref="segmentOrder"/>, keeping the walk order
        /// within each segment and ordering the segments by where each first appears in the walk.
        /// </summary>
        private void UpdateSegmentData(ParticleSpan particles)
        {
            segmentRepeats.Clear();
            segmentStarts.Clear();

            var count = particles.Length;
            var segments = particles.ReadFloats(splitField);
            var segmentData = particles.Vectors(ParticleField.RopeSegmentData);

            for (var i = 0; i < count; i++)
            {
                var segmentBits = BitConverter.SingleToInt32Bits(segments[i]);
                var place = segmentRepeats.TryGetValue(segmentBits, out var repeats) ? repeats + 1 : 0;

                segmentRepeats[segmentBits] = place;
                segmentData[i] = new Vector3(place, 0f, 0f);
            }

            var nextStart = 0;

            for (var n = 0; n < count; n++)
            {
                var segmentBits = BitConverter.SingleToInt32Bits(segments[WalkIndex(n, count)]);

                if (segmentStarts.TryAdd(segmentBits, nextStart))
                {
                    nextStart += segmentRepeats[segmentBits] + 1;
                }
            }

            if (segmentOrder.Length < count)
            {
                segmentOrder = new int[count];
            }

            for (var i = 0; i < count; i++)
            {
                var segmentBits = BitConverter.SingleToInt32Bits(segments[i]);
                var last = segmentRepeats[segmentBits];
                var place = (int)segmentData[i].X;
                var rank = reverseOrder ? last - place : place;

                segmentOrder[segmentStarts[segmentBits] + rank] = i;
                segmentData[i] = new Vector3(place, last > 0 ? place / (float)last : 0f, last);
            }

            segmentOrderCount = count;
        }

        /// <summary>The particle the rope visits <paramref name="n"/>-th, from the last one when reversed.</summary>
        private int WalkIndex(int n, int count) => reverseOrder ? count - 1 - n : n;

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
        /// Picks one subdivision level for the whole collection from its projected size: the level of the
        /// smallest power of two at least the subdivision count. The count is lowered to the ceiling before
        /// the floor is applied, so the floor wins when it is above the ceiling.
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
            var coverage = ScreenSizeFraction((min + max) * 0.5f, diagonal, camera);

            var metric = MathF.Sqrt(coverage) * 1024f / nodes.Length;
            var raw = (int)(metric * tessScale * 0.1f);

            var subdivisions = Math.Min(raw, maxTessellation);

            if (minTessellation > subdivisions)
            {
                subdivisions = minTessellation;
            }

            subdivisions = Math.Clamp(subdivisions, 1, MaxSubdivisions);

            return subdivisions == 1 ? 0 : BitOperations.Log2((uint)(subdivisions - 1)) + 1;
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
        /// Copies the live particles into <paramref name="nodes"/> in walk order, grouped by segment when
        /// sorted. The radius is before the size clamp and the alpha before the fades, which the shader applies.
        /// </summary>
        /// <remarks>
        /// The tint is truncated to bytes without a clamp, so a channel past 1 wraps around. The alpha
        /// scale applies twice: once with the particle's alpha and again where the colour scale combines.
        /// </remarks>
        private int CollectNodes(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera, Span<RopeNode> nodes)
        {
            var count = particleBag.Count;
            var grouped = sortBySegmentId && segmentOrderCount == count;
            var splits = Splits;
            var scales = appliesVisibility ? EvaluateScales(systemState, camera) : EvaluateScales(systemState);

            // The visible alpha goes in with the alpha scale, both times it applies
            var alphaScale = scales.Alpha * scales.VisibleAlpha;
            var colorScale = scales.Color with { W = alphaScale };

            if (scales.Radius == 0f || alphaScale == 0f)
            {
                return 0;
            }

            var particles = particleBag.Current;
            var positions = particles.ReadVectors(ParticleField.Position);
            var colors = particles.ReadVectors(ParticleField.Color);
            var orientations = particles.ReadVectors(orientationField);
            var radii = particles.ReadFloats(ParticleField.Radius);
            var alphas = particles.ReadFloats(ParticleField.Alpha);
            var alpha2s = ReadAlpha2(particles);
            var segments = splits ? particles.ReadFloats(splitField) : default;
            var scalarCoordinates = useScalarForTextureCoordinate ? particles.ReadFloats(scalarFieldForTextureCoordinate) : default;
            var extraData1 = particles.ReadFloats(ParticleField.ShaderExtraData1);
            var extraData2 = particles.ReadFloats(ParticleField.ShaderExtraData2);
            var rolls = particles.ReadFloats(ParticleField.Roll);
            var yaws = particles.ReadFloats(ParticleField.Yaw);
            var pitches = particles.ReadFloats(ParticleField.Pitch);

            for (var i = 0; i < count; i++)
            {
                var particleIndex = grouped ? segmentOrder[i] : WalkIndex(i, count);

                var radius = radii[particleIndex] * scales.Radius;
                var color = ParticleColorBlend.Apply(ColorBlendType,
                    ParticleColorBlend.ToWrappedBytes(colors[particleIndex], Math.Clamp(alphaScale * 255f * alpha2s[particleIndex] * alphas[particleIndex], 0f, 255f)),
                    colorScale) / 255f;

                nodes[i] = new RopeNode
                {
                    ParticleIndex = particleIndex,
                    SegmentBits = splits ? BitConverter.SingleToInt32Bits(segments[particleIndex]) : 0,
                    Position = positions[particleIndex],
                    Radius = radius,
                    Color = color.AsVector3(),
                    Alpha = color.W,
                    Orientation = orientations[particleIndex],
                    ScalarCoordinate = useScalarForTextureCoordinate
                        ? scalarCoordinates[particleIndex] * scalarAttributeTextureCoordScale
                        : 0f,
                    ExtraData1 = extraData1[particleIndex],
                    ExtraData2 = extraData2[particleIndex],
                    Rotation = new Vector3(rolls[particleIndex], yaws[particleIndex], pitches[particleIndex]),
                };
            }

            return count;
        }

        /// <summary>
        /// Builds the ribbon into the shared vertex buffer and returns how many quads were written.
        /// V is raw world arc length measured between the particles themselves, so subdividing a
        /// segment keeps the same V span as the straight chord it replaces. A segment joining two
        /// rope segments, or one of no length, is not drawn, but its length still advances V. The
        /// segment of an open rope that ends on the first particle ends at V 0.
        /// </summary>
        protected override int UpdateVertices(ParticleCollection particleBag, ParticleSystemState systemState, Camera camera)
        {
            using var nodeBuffer = new RentedBuffer<RopeNode>(particleBag.Count);
            var nodeCount = CollectNodes(particleBag, systemState, camera, nodeBuffer.Span);

            // A frame with no radius or alpha scale returns before the carried V offset sees a new first
            // particle, where only a collection of fewer than two particles should skip it
            if (nodeCount < 2)
            {
                return 0;
            }

            var nodes = nodeBuffer.Span[..nodeCount];
            var segmentCount = closedLoop ? nodeCount : nodeCount - 1;
            var subdivisions = 1 << ComputeTessellationLevel(nodes, camera);

            var (scrollRate, vOffset, oneOverWorldSize) = ResolveTextureV(systemState);
            CarryVOffset(particleBag, oneOverWorldSize);
            vMapping = new Vector2(vOffset, oneOverWorldSize);

            // A first layer showing a sheet places its frames by the card coordinate, and V stays 0
            var lengthV = !Layers[0].HasSheet;

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

                    // A hidden join is skipped rather than drawn with no alpha
                    if (n1.SegmentBits != n2.SegmentBits || segmentLength == 0f)
                    {
                        stripAxis = Vector3.Zero;
                        continue;
                    }

                    ref readonly var before = ref nodes[ResolveIndex(segment - 1, nodeCount)];
                    ref readonly var after = ref nodes[ResolveIndex(segment + 2, nodeCount)];
                    ref readonly var n0 = ref before.SegmentBits == n1.SegmentBits ? ref before : ref n1;
                    ref readonly var n3 = ref after.SegmentBits == n2.SegmentBits ? ref after : ref n2;

                    if (!lengthV)
                    {
                        vStart = 0f;
                        vEnd = 0f;
                    }
                    else if (useScalarForTextureCoordinate)
                    {
                        vStart = n1.ScalarCoordinate;
                        vEnd = n2.ParticleIndex == 0 ? n1.ScalarCoordinate : n2.ScalarCoordinate;
                    }

                    var previous = EvaluateSample(n0, n1, n2, n3, 0f, camera);
                    AlignWidthAxis(ref previous, ref stripAxis);

                    for (var step = 1; step <= subdivisions && quadCount < maxQuads; step++)
                    {
                        var previousT = (step - 1) / (float)subdivisions;
                        var t = step / (float)subdivisions;
                        var current = EvaluateSample(n0, n1, n2, n3, t, camera);
                        AlignWidthAxis(ref current, ref stripAxis);

                        WriteQuad(vertices, quadCount, n1, previous, current, previousT, t,
                            MathUtils.Lerp(vStart, vEnd, previousT),
                            MathUtils.Lerp(vStart, vEnd, t),
                            systemState.Age);

                        quadCount++;
                        previous = current;
                    }
                }

                if (quadCount > 0)
                {
                    UploadVertices(vertexBuffer.ByteArray, quadCount * 4);
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
            var particleId = particleBag.Current.ReadInteger(ParticleField.ParticleId, 0);

            if (firstParticleId != -1 && particleId != firstParticleId)
            {
                vOffsetAccumulator = (vOffsetAccumulator + firstSegmentLength) % (1f / oneOverWorldSize);
            }

            firstParticleId = particleId;
        }

        /// <summary>
        /// Splines position, radius and colour between the two middle control points and builds the
        /// ribbon's width axis from the spline tangent and the plane the orientation type selects.
        /// </summary>
        private RibbonSample EvaluateSample(in RopeNode n0, in RopeNode n1, in RopeNode n2, in RopeNode n3, float t, Camera camera)
        {
            var position = CableMeshBuilder.CatmullRom(n0.Position, n1.Position, n2.Position, n3.Position, t);
            var radius = CableMeshBuilder.CatmullRom(n0.Radius, n1.Radius, n2.Radius, n3.Radius, t);

            var tangent = CableMeshBuilder.CatmullRomTangent(n0.Position, n1.Position, n2.Position, n3.Position, t);
            tangent = MathUtils.SafeNormalize(tangent, Vector3.UnitX, ParticleMath.MinimumLengthSquared);

            var normalAligned = orientationType == ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL;
            var orientation = Vector3.Lerp(n1.Orientation, n2.Orientation, t);

            var planeNormal = orientationType switch
            {
                ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => orientation,
                ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => Vector3.UnitZ,
                _ => camera.Location - position,
            };

            // The node colours and alphas are already in [0, 1], so their blend is too
            return new RibbonSample
            {
                Position = position,
                WidthAxis = RibbonWidthAxis(tangent, planeNormal),
                FacingAxis = normalAligned ? orientation : tangent,
                Radius = radius,
                Color = new Vector4(Vector3.Lerp(n1.Color, n2.Color, t), MathUtils.Lerp(n1.Alpha, n2.Alpha, t)),
            };
        }

        /// <summary>
        /// Writes the four corners between two samples of the segment that starts at
        /// <paramref name="segmentStart"/>, which every corner takes its particle values from. The
        /// samples lie <paramref name="startT"/> and <paramref name="endT"/> along the segment, which
        /// places them along the card, and have the raw V <paramref name="vStart"/> and <paramref name="vEnd"/>.
        /// </summary>
        private static void WriteQuad(Span<SpritecardVertex> vertices, int quadIndex, in RopeNode segmentStart,
            in RibbonSample start, in RibbonSample end, float startT, float endT, float vStart, float vEnd, float sheetAge)
        {
            // The card runs from its top at the segment start to its bottom at the segment end
            var startCornerY = 1f - (2f * startT);
            var endCornerY = 1f - (2f * endT);

            // A rope hands over no random value, and the system's age in place of the particle's
            var sheetInputs = new Vector4(0f, segmentStart.ExtraData1, sheetAge, segmentStart.ExtraData2);

            var quadStart = quadIndex * 4;

            WriteCorner(ref vertices[quadStart], start, -1f, startCornerY, vStart, segmentStart, sheetInputs);
            WriteCorner(ref vertices[quadStart + 1], end, -1f, endCornerY, vEnd, segmentStart, sheetInputs);
            WriteCorner(ref vertices[quadStart + 2], end, 1f, endCornerY, vEnd, segmentStart, sheetInputs);
            WriteCorner(ref vertices[quadStart + 3], start, 1f, startCornerY, vStart, segmentStart, sheetInputs);
        }

        /// <summary>
        /// Writes one corner of a quad, on the side of the ribbon <paramref name="side"/> picks: -1 against
        /// the sample's width axis, 1 along it.
        /// </summary>
        private static void WriteCorner(ref SpritecardVertex vertex, in RibbonSample sample, float side, float cornerY, float v,
            in RopeNode segmentStart, Vector4 sheetInputs)
        {
            var widthAxis = sample.WidthAxis * side;

            vertex = new SpritecardVertex
            {
                Position = sample.Position + (widthAxis * sample.Radius),
                Color = sample.Color,
                SheetInputs = sheetInputs,
                CornerV = new Vector3(side, cornerY, v),
                RotationRadius = new Vector4(segmentStart.Rotation, sample.Radius),
                WidthAxis = widthAxis,
                FacingAxis = sample.FacingAxis,
                SegmentAlphaRadius = new Vector2(segmentStart.Alpha, segmentStart.Radius),
            };
        }

        /// <summary>
        /// Keeps the ribbon side vector on one hemisphere along the strip. The sign of the width axis
        /// is visually symmetric per sample, but letting it flip between neighboring samples folds
        /// their shared quad into a bowtie where the tangent passes through the view direction.
        /// </summary>
        private static void AlignWidthAxis(ref RibbonSample sample, ref Vector3 stripAxis)
        {
            if (Vector3.Dot(sample.WidthAxis, stripAxis) < 0f)
            {
                sample.WidthAxis = -sample.WidthAxis;
            }

            stripAxis = sample.WidthAxis;
        }

        /// <inheritdoc/>
        protected override void SetDrawUniforms(ParticleSystemState systemState)
        {
            SetSizeClampAndFadeUniforms(enableFadingAndClamping,
                minSize,
                startFadeSize.NextNumber(systemState),
                endFadeSize.NextNumber(systemState),
                maxSize);

            // The view angle fade runs in every mode and outside the fade-and-clamp gate
            SetViewAngleFadeUniforms(FadesByViewAngle);

            // A rope does not zoom between sheet frames
            Shader.SetUniform2("uFrameZoom", Vector2.One);

            // TODO: Where the V clamp applies is not known; it holds the mapped V to 0-1
            Shader.SetUniform2("uRopeVMapping", vMapping);
            Shader.SetUniform1("uClampV", clampV);

            base.SetDrawUniforms(systemState);
        }

        /// <inheritdoc/>
        protected override void Draw()
        {
            GL.DrawElements(PrimitiveType.Triangles, QuadCount * 6, DrawElementsType.UnsignedShort, 0);
        }

        /// <inheritdoc/>
        public override bool CanRenderReplacement => true;

        /// <inheritdoc/>
        public override void RenderReplacement(Shader replacement, uint objectId)
            => DrawReplacement(replacement, objectId, VertexArrayHandle, QuadCount * 6, DrawElementsType.UnsignedShort);
    }
}
