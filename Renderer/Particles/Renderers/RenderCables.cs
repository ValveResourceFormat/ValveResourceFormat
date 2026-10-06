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

        private readonly RenderMaterial material;
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
        // grow-only; only the first lastCount (lastCount - 1 for levels) entries are valid.
        private int indexCount;
        private int lastCount;
        private Vector3[] lastPositions = [];
        private int[] lastLevels = [];
        private float[] lastRadii = [];
        private Vector3[] lastColors = [];

        // Per-frame scratch reused across frames so a settled cable allocates nothing. Grow-only,
        // sliced to the live particle (or segment) count each frame.
        private (int Id, Vector3 Position, float Radius, Vector3 Color)[] chainScratch = [];
        private Vector3[] positionsScratch = [];
        private float[] radiiScratch = [];
        private Vector3[] colorsScratch = [];
        private int[] levelsScratch = [];
        private Vector3[] directionsScratch = [];

        private static readonly Comparison<(int Id, Vector3 Position, float Radius, Vector3 Color)> ChainComparer =
            static (a, b) => a.Id.CompareTo(b.Id);

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
            material = rendererContext.MaterialLoader.GetMaterial(materialName, CreateShaderArguments());

            Pass = material.IsTranslucent ? RenderPass.Translucent : RenderPass.Opaque;
            CanRenderDepth = Pass == RenderPass.Opaque && !OnlyRenderInEffectsWaterPass && material.Shader.DepthMode != null;

            vaoHandle = SetupBuffers();
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

            // Order the live particles along the rope by particle id; prune compaction can reshuffle slots.
            var count = particles.Count;
            chainScratch = EnsureCapacity(chainScratch, count);
            var chain = chainScratch.AsSpan(0, count);
            var index = 0;
            foreach (ref var particle in particles.Current)
            {
                chain[index++] = (particle.UniqueParticleId, particle.Position, particle.Radius, particle.Color);
            }

            chain.Sort(ChainComparer);

            positionsScratch = EnsureCapacity(positionsScratch, count);
            radiiScratch = EnsureCapacity(radiiScratch, count);
            colorsScratch = EnsureCapacity(colorsScratch, count);
            var positions = positionsScratch.AsSpan(0, count);
            var radii = radiiScratch.AsSpan(0, count);
            var colors = colorsScratch.AsSpan(0, count);
            for (var i = 0; i < count; i++)
            {
                positions[i] = chain[i].Position;
                radii[i] = chain[i].Radius;
                colors[i] = chain[i].Color;
            }

            var levels = ComputeTessellationLevels(positions, camera);

            if (!GeometryChanged(positions, levels, radii, colors))
            {
                return;
            }

            lastCount = count;
            lastPositions = EnsureCapacity(lastPositions, count);
            lastLevels = EnsureCapacity(lastLevels, count - 1);
            lastRadii = EnsureCapacity(lastRadii, count);
            lastColors = EnsureCapacity(lastColors, count);
            positions.CopyTo(lastPositions);
            levels.CopyTo(lastLevels);
            radii.CopyTo(lastRadii);
            colors.CopyTo(lastColors);

            var repeatsPerSegment = textureRepeatsPerSegment.NextNumber(systemState);
            var circumference = circumferenceRepeats.NextNumber(systemState);

            // Only the fractional part of each offset is used, truncated toward zero.
            var colorMapOffset = new Vector2(colorMapOffsetU.NextNumber(systemState) % 1f, colorMapOffsetV.NextNumber(systemState) % 1f);

            // In PATH mode the authored repeat count is spread over the whole cable instead of per segment.
            var repeats = textureRepetitionMode == TextureRepetitionMode.TEXTURE_REPETITION_PATH
                ? repeatsPerSegment / (chain.Length - 1)
                : repeatsPerSegment;

            // Exact buffer sizes are known up front, so the transient build buffers are rented, filled by
            // index and returned; the rented arrays may be larger, so sizes are threaded through explicitly.
            var ringCount = TotalRings(positions.Length, levels);
            var sides = CableMeshBuilder.SideCount(roundness);
            var vertexCount = ringCount * (sides + 1);
            var tubeIndexCount = (ringCount - 1) * sides * 6;

            using var ringPositions = new RentedBuffer<Vector3>(ringCount);
            using var ringSamples = new RentedBuffer<RopeSample>(ringCount);
            using var vertexBuffer = new RentedBuffer<CableVertex>(vertexCount);
            using var indexBuffer = new RentedBuffer<uint>(tubeIndexCount);

            BuildRings(positions, chain, levels, repeats, ringPositions.Span, ringSamples.Span);

            if (!CableMeshBuilder.BuildTubeMesh(ringPositions.Span, ringSamples.Span,
                sides, circumference, colorMapOffset, vertexBuffer.Span, indexBuffer.Span))
            {
                indexCount = 0;
                return;
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
        private Span<int> ComputeTessellationLevels(ReadOnlySpan<Vector3> positions, Camera camera)
        {
            var segmentCount = positions.Length - 1;
            levelsScratch = EnsureCapacity(levelsScratch, segmentCount);
            var levels = levelsScratch.AsSpan(0, segmentCount);

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

            // Guard against pathological totals by stepping every level down together.
            for (var pass = 0; pass < MaxTessellationLevel && TotalRings(positions.Length, levels) > MaxTubeRings; pass++)
            {
                for (var i = 0; i < segmentCount; i++)
                {
                    levels[i] = Math.Max(0, levels[i] - 1);
                }
            }

            return levels;
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
        private static void BuildRings(ReadOnlySpan<Vector3> positions, ReadOnlySpan<(int Id, Vector3 Position, float Radius, Vector3 Color)> chain,
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
                        (i + t) * repeats, false);
                    cursor++;
                }
            }

            ringPositions[cursor] = positions[last];
            ringSamples[cursor] = new RopeSample(positions[last], chain[last].Radius, chain[last].Color, last * repeats, false);
        }

        private bool GeometryChanged(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> levels, ReadOnlySpan<float> radii, ReadOnlySpan<Vector3> colors)
        {
            // The count check guards the slices below: when it passes, lastCount >= 2.
            return positions.Length != lastCount
                || !levels.SequenceEqual(lastLevels.AsSpan(0, lastCount - 1))
                || !radii.SequenceEqual(lastRadii.AsSpan(0, lastCount))
                || !colors.SequenceEqual(lastColors.AsSpan(0, lastCount))
                || !positions.SequenceEqual(lastPositions.AsSpan(0, lastCount));
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
