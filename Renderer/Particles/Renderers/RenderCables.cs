using System.Diagnostics.CodeAnalysis;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders the ordered particle chain as a lit round tube. The tube tessellation is shared with the
    /// map <c>path_particle_rope</c> rendering path via <see cref="CableMeshBuilder"/>. Geometry is rebuilt
    /// whenever the particle positions, radii, colors, or the per-segment tessellation levels change.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderCables">C_OP_RenderCables</seealso>
    internal class RenderCables : ParticleFunctionRenderer
    {
        private const string DefaultMaterialName = "particles/dev/dev_cables_preview_material.vmat";

        private RenderMaterial material;
        private readonly int vaoHandle;
        private int vertexBufferHandle;
        private int indexBufferHandle;

        private const int MaxTessellationLevel = 7;
        private const int MaxTubeRings = 8192;

        // A segment is sized for tessellation by its bounding sphere, whose diameter is the segment length
        // scaled by this (the default r_particle_cables_culling_bounds_scale).
        private const float SegmentBoundsScale = 1.2f;

        private readonly int roundness = 1;
        private readonly TextureRepetitionMode textureRepetitionMode;
        private readonly INumberProvider textureRepeatsPerSegment = new LiteralNumberProvider(1f);
        private readonly INumberProvider circumferenceRepeats = new LiteralNumberProvider(1f);
        private readonly INumberProvider colorMapOffsetU = new LiteralNumberProvider(0f);
        private readonly INumberProvider colorMapOffsetV = new LiteralNumberProvider(0f);
        private readonly float tessScale = 1f;
        private readonly int minTessellation = 1;
        private readonly int maxTessellation = 128;

        // Cached tube so a settled/static cable is not re-tessellated every frame. The arrays are
        // grow-only; only the first lastCount entries are valid.
        private int indexCount;
        private int lastCount;
        private CableNode[] lastNodes = [];
        private int[] lastLevels = [];

        // Per-frame scratch reused across frames so a settled cable allocates nothing. Grow-only,
        // sliced to the live particle count each frame.
        private CableNode[] chainScratch = [];
        private Vector3[] positionsScratch = [];
        private int[] levelsScratch = [];
        private Vector3[] directionsScratch = [];

        /// <summary>
        /// One particle of the chain. <see cref="Order"/> is the unique particle id, which orders the
        /// particles along their rope however prune compaction has reshuffled their slots.
        /// </summary>
        private readonly record struct CableNode(int SegmentId, int Order, Vector3 Position, float Radius, Vector3 Color, float Alpha) : IRopeChainEntry;

        public RenderCables(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse, scene)
        {
            roundness = parse.Int32("m_nRoundness", roundness);
            textureRepetitionMode = parse.Enum("m_nTextureRepetitionMode", textureRepetitionMode);
            tessScale = parse.Float("m_flTessScale", tessScale);
            minTessellation = parse.Int32("m_nMinTesselation", minTessellation);
            maxTessellation = parse.Int32("m_nMaxTesselation", maxTessellation);
            textureRepeatsPerSegment = parse.NumberProvider("m_flTextureRepeatsPerSegment", textureRepeatsPerSegment);
            circumferenceRepeats = parse.NumberProvider("m_flTextureRepeatsCircumference", circumferenceRepeats);
            colorMapOffsetU = parse.NumberProvider("m_flColorMapOffsetU", colorMapOffsetU);
            colorMapOffsetV = parse.NumberProvider("m_flColorMapOffsetV", colorMapOffsetV);

            var materialName = parse.Data.GetStringProperty("m_hMaterial", DefaultMaterialName);
            UseMaterial(rendererContext.MaterialLoader.GetMaterial(materialName, CreateShaderArguments()));

            vaoHandle = SetupBuffers();
        }

        /// <inheritdoc/>
        public override void SetMaterialOverride(RenderMaterial material) => UseMaterial(material);

        [MemberNotNull(nameof(material))]
        private void UseMaterial(RenderMaterial newMaterial)
        {
            material = newMaterial;
            Pass = material.IsTranslucent ? RenderPass.Translucent : RenderPass.Opaque;
            CanRenderDepth = Pass == RenderPass.Opaque && !OnlyRenderInEffectsWaterPass && material.Shader.DepthMode != null;
        }

        private int SetupBuffers()
        {
            vertexBufferHandle = GraphicsDevice.CreateBuffer(nameof(RenderCables));
            indexBufferHandle = GraphicsDevice.CreateBuffer(nameof(RenderCables));

            return CableVertex.InputLayout.CreateVertexArray(nameof(RenderCables), vertexBufferHandle, indexBufferHandle);
        }

        /// <inheritdoc/>
        public override void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            if (particles.Count < 2)
            {
                indexCount = 0;
                lastCount = 0;
                return;
            }

            var count = particles.Count;
            chainScratch = EnsureCapacity(chainScratch, count);
            var chain = chainScratch.AsSpan(0, count);
            var index = 0;
            foreach (ref var particle in particles.Current)
            {
                chain[index++] = new CableNode(particle.RopeSegmentId, particle.UniqueParticleId, particle.Position, particle.Radius, particle.Color, particle.Alpha);
            }

            RopeSegmentRuns.Group(chain);

            positionsScratch = EnsureCapacity(positionsScratch, count);
            var positions = positionsScratch.AsSpan(0, count);
            for (var i = 0; i < count; i++)
            {
                positions[i] = chain[i].Position;
            }

            // A run of n particles has n - 1 segments; its last slot is left at 0 so the levels line up
            // with the chain and every run reads its own slice.
            levelsScratch = EnsureCapacity(levelsScratch, count);
            var levels = levelsScratch.AsSpan(0, count);

            for (var start = 0; start < count;)
            {
                var end = RopeSegmentRuns.RunEnd<CableNode>(chain, start);

                if (end - start >= 2)
                {
                    ComputeTessellationLevels(positions[start..end], levels[start..(end - 1)], camera);
                }

                levels[end - 1] = 0;
                start = end;
            }

            CapTotalRings(chain, levels);

            if (!GeometryChanged(chain, levels))
            {
                return;
            }

            lastCount = count;
            lastNodes = EnsureCapacity(lastNodes, count);
            lastLevels = EnsureCapacity(lastLevels, count);
            chain.CopyTo(lastNodes);
            levels.CopyTo(lastLevels);

            var repeatsPerSegment = textureRepeatsPerSegment.NextNumber(systemState);
            var circumference = circumferenceRepeats.NextNumber(systemState);

            // Only the fractional part of each offset is used, truncated toward zero.
            var colorMapOffset = new Vector2(colorMapOffsetU.NextNumber(systemState) % 1f, colorMapOffsetV.NextNumber(systemState) % 1f);

            // Exact buffer sizes are known up front, so the transient build buffers are rented, filled by
            // index and returned; the rented arrays may be larger, so sizes are threaded through explicitly.
            var sides = CableMeshBuilder.SideCount(roundness);
            var ringCount = TotalRings(chain, levels);
            var vertexCount = ringCount * (sides + 1);
            var tubeIndexCount = 0;

            for (var start = 0; start < count;)
            {
                var end = RopeSegmentRuns.RunEnd<CableNode>(chain, start);

                if (end - start >= 2)
                {
                    tubeIndexCount += (TotalRings(end - start, levels[start..(end - 1)]) - 1) * sides * 6;
                }

                start = end;
            }

            if (tubeIndexCount == 0)
            {
                indexCount = 0;
                return;
            }

            using var ringPositions = new RentedBuffer<Vector3>(ringCount);
            using var ringSamples = new RentedBuffer<RopeSample>(ringCount);
            using var vertexBuffer = new RentedBuffer<CableVertex>(vertexCount);
            using var indexBuffer = new RentedBuffer<uint>(tubeIndexCount);

            var ringCursor = 0;
            var indexCursor = 0;

            for (var start = 0; start < count;)
            {
                var end = RopeSegmentRuns.RunEnd<CableNode>(chain, start);
                var length = end - start;

                if (length < 2)
                {
                    start = end;
                    continue;
                }

                var runLevels = levels[start..(end - 1)];
                var rings = TotalRings(length, runLevels);

                // In PATH mode the authored repeat count is spread over the whole rope instead of per segment.
                var repeats = textureRepetitionMode == TextureRepetitionMode.TEXTURE_REPETITION_PATH
                    ? repeatsPerSegment / (length - 1)
                    : repeatsPerSegment;

                var runRingPositions = ringPositions.Span.Slice(ringCursor, rings);
                var runRingSamples = ringSamples.Span.Slice(ringCursor, rings);
                BuildRings(positions[start..end], chain[start..end], runLevels, repeats, runRingPositions, runRingSamples);

                var vertexStart = ringCursor * (sides + 1);
                var runIndices = indexBuffer.Span.Slice(indexCursor, (rings - 1) * sides * 6);

                CableMeshBuilder.BuildTubeMesh(runRingPositions, runRingSamples, sides, circumference, colorMapOffset,
                    vertexBuffer.Span.Slice(vertexStart, rings * (sides + 1)), runIndices);

                // Each tube indexes its own vertices from zero; move them past the tubes before it.
                for (var i = 0; i < runIndices.Length; i++)
                {
                    runIndices[i] += (uint)vertexStart;
                }

                ringCursor += rings;
                indexCursor += runIndices.Length;
                start = end;
            }

            var stride = CableVertex.InputLayout.Stride;
            GL.NamedBufferData(vertexBufferHandle, vertexCount * stride, vertexBuffer.ByteArray, BufferUsageHint.DynamicDraw);
            GL.NamedBufferData(indexBufferHandle, tubeIndexCount * sizeof(uint), indexBuffer.ByteArray, BufferUsageHint.DynamicDraw);
            indexCount = tubeIndexCount;
        }

        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            DrawTube(material.Shader);
        }

        /// <inheritdoc/>
        public override void RenderDepth(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            if (material.Shader.DepthMode is { } depthShader)
            {
                DrawTube(depthShader);
            }
        }

        /// <inheritdoc/>
        public override bool CanRenderReplacement => true;

        /// <inheritdoc/>
        public override void RenderReplacement(Shader replacement, uint objectId)
            => DrawReplacement(replacement, objectId, vaoHandle, indexCount, DrawElementsType.UnsignedInt);

        /// <summary>
        /// Per-segment length tessellation: the apparent on-screen size of the segment's bounding sphere scaled
        /// by m_flTessScale picks a power-of-two subdivision count within [m_nMinTesselation, m_nMaxTesselation],
        /// bumped one or two levels where adjacent segments bend sharply, then clamped between its neighbours.
        /// </summary>
        private void ComputeTessellationLevels(ReadOnlySpan<Vector3> positions, Span<int> levels, Camera camera)
        {
            var segmentCount = positions.Length - 1;

            // Scaled against a 1920 reference resolution, in integer math.
            var resolutionScale = (int)camera.WindowSize.Y * 64 / 1920;

            directionsScratch = EnsureCapacity(directionsScratch, segmentCount);
            var directions = directionsScratch.AsSpan(0, segmentCount);
            for (var i = 0; i < segmentCount; i++)
            {
                var direction = positions[i + 1] - positions[i];
                directions[i] = MathUtils.SafeNormalize(direction, Vector3.UnitX, ParticleMath.MinimumLengthSquared);
            }

            for (var i = 0; i < segmentCount; i++)
            {
                var boundsRadius = Vector3.Distance(positions[i], positions[i + 1]) * SegmentBoundsScale * 0.5f;
                var midpoint = (positions[i] + positions[i + 1]) * 0.5f;
                var distanceSquared = Vector3.DistanceSquared(midpoint, camera.Location);

                // Apparent bounds radius as a fraction of the viewport; full when the camera is inside them.
                var size = boundsRadius * boundsRadius > distanceSquared
                    ? 1f
                    : MathUtils.Saturate(boundsRadius * camera.ProjectionMatrix.M22 / MathF.Sqrt(distanceSquared));

                var tess = size * tessScale * resolutionScale;
                var subdivisions = Math.Clamp(MathUtils.Clamp((int)tess, minTessellation, maxTessellation), 1, 1 << MaxTessellationLevel);
                var level = BitOperations.Log2((uint)subdivisions);

                var bendPrev = i > 0 ? Vector3.Dot(directions[i], directions[i - 1]) : 1f;
                var bendNext = i < segmentCount - 1 ? Vector3.Dot(directions[i], directions[i + 1]) : 1f;
                var bend = 1f - Math.Clamp(MathF.Min(bendPrev, bendNext), -1f, 1f);

                if (tess > 0.1f && bend > 0.8f)
                {
                    level += 2;
                }
                else if (tess > 0.1f && bend > 0.1f)
                {
                    level += 1;
                }

                levels[i] = Math.Clamp(level, 0, MaxTessellationLevel);
            }

            // Clamped in place, so each segment already sees its filtered predecessor.
            for (var i = 1; i < segmentCount - 1; i++)
            {
                var previous = levels[i - 1];
                var next = levels[i + 1];
                levels[i] = Math.Clamp(levels[i], Math.Min(previous, next), Math.Max(previous, next));
            }
        }

        // Guards against pathological totals by stepping every level of every run down together.
        private static void CapTotalRings(ReadOnlySpan<CableNode> chain, Span<int> levels)
        {
            for (var pass = 0; pass < MaxTessellationLevel && TotalRings(chain, levels) > MaxTubeRings; pass++)
            {
                for (var i = 0; i < levels.Length; i++)
                {
                    levels[i] = Math.Max(0, levels[i] - 1);
                }
            }
        }

        // Rings over every run of the chain; a run too short to draw has none.
        private static int TotalRings(ReadOnlySpan<CableNode> chain, ReadOnlySpan<int> levels)
        {
            var total = 0;

            for (var start = 0; start < chain.Length;)
            {
                var end = RopeSegmentRuns.RunEnd(chain, start);

                if (end - start >= 2)
                {
                    total += TotalRings(end - start, levels[start..(end - 1)]);
                }

                start = end;
            }

            return total;
        }

        private static int TotalRings(int nodeCount, ReadOnlySpan<int> levels)
        {
            var total = nodeCount;
            foreach (var level in levels)
            {
                total += (1 << level) - 1;
            }

            return total;
        }

        // Expands each particle segment into 2^level rings. Position and radius follow a Catmull-Rom spline
        // through the particles, with the end particles repeated past both ends; colour and V are linear.
        // Fills exactly TotalRings entries of the (pooled, possibly larger) output arrays.
        private static void BuildRings(ReadOnlySpan<Vector3> positions, ReadOnlySpan<CableNode> chain,
            ReadOnlySpan<int> levels, float repeats, Span<Vector3> ringPositions, Span<RopeSample> ringSamples)
        {
            var cursor = 0;
            var last = positions.Length - 1;

            for (var i = 0; i < levels.Length; i++)
            {
                var before = Math.Max(i - 1, 0);
                var after = Math.Min(i + 2, last);

                var subdivisions = 1 << levels[i];
                for (var s = 0; s < subdivisions; s++)
                {
                    var t = s / (float)subdivisions;
                    var position = CableMeshBuilder.CatmullRom(positions[before], positions[i], positions[i + 1], positions[after], t);
                    ringPositions[cursor] = position;

                    // Particles are roughly evenly spaced, so index-based V tracks arc length.
                    ringSamples[cursor] = new RopeSample(position,
                        CableMeshBuilder.CatmullRom(chain[before].Radius, chain[i].Radius, chain[i + 1].Radius, chain[after].Radius, t),
                        Vector3.Lerp(chain[i].Color, chain[i + 1].Color, t),
                        (i + t) * repeats, false, float.Lerp(chain[i].Alpha, chain[i + 1].Alpha, t));
                    cursor++;
                }
            }

            ringPositions[cursor] = positions[last];
            ringSamples[cursor] = new RopeSample(positions[last], chain[last].Radius, chain[last].Color, last * repeats, false, chain[last].Alpha);
        }

        private bool GeometryChanged(ReadOnlySpan<CableNode> chain, ReadOnlySpan<int> levels)
        {
            return chain.Length != lastCount
                || !levels.SequenceEqual(lastLevels.AsSpan(0, lastCount))
                || !chain.SequenceEqual(lastNodes.AsSpan(0, lastCount));
        }

        // Grow-only: reused buffers are sliced to the live count, so shrinking never reallocates.
        private static T[] EnsureCapacity<T>(T[] buffer, int size) => buffer.Length >= size ? buffer : new T[size];

        private void DrawTube(Shader drawShader)
        {
            if (indexCount == 0)
            {
                return;
            }

            drawShader.Use();
            VertexArray.Bind(vaoHandle, drawShader);
            material.Render(drawShader);

            PerfStats.Active.Count(Counter.ParticleDraw);

            GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0, 1, OwnerNode?.Id ?? 0);

            material.PostRender();
        }

        public override IEnumerable<string> GetSupportedRenderModes() => material.Shader.RenderModes;

        public override void Delete()
        {
            VertexArray.Delete(vaoHandle);
            GL.DeleteBuffer(vertexBufferHandle);
            GL.DeleteBuffer(indexBufferHandle);
        }
    }
}
