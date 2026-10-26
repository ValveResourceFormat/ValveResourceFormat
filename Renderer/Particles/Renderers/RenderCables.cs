using System.Diagnostics.CodeAnalysis;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders the particles, in collection order, as one lit round tube, closed by domed caps when asked
    /// to. The tube tessellation is shared
    /// with the map <c>path_particle_rope</c> rendering path via <see cref="CableMeshBuilder"/>. Geometry is
    /// rebuilt whenever the particle positions, radii, colors, alphas, or the per-segment tessellation
    /// levels change. Radius, alpha and colour are scaled by <c>m_flRadiusScale</c>, <c>m_flAlphaScale</c>
    /// and <c>m_vecColorScale</c>, evaluated once for the system; the alpha scale applies twice. The tint
    /// is truncated to bytes without a clamp, so a channel past 1 wraps around.
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
        // scaled by this.
        private const float SegmentBoundsScale = 1.2f;

        private readonly int roundness = 1;
        private readonly TextureRepetitionMode textureRepetitionMode;
        private readonly INumberProvider textureRepeatsPerSegment = new LiteralNumberProvider(1f);
        private readonly INumberProvider circumferenceRepeats = new LiteralNumberProvider(1f);
        private readonly INumberProvider colorMapOffsetU = new LiteralNumberProvider(0f);
        private readonly INumberProvider colorMapOffsetV = new LiteralNumberProvider(0f);

        private readonly bool drawCableCaps;

        /// <summary>How round the caps are, from 0 to 2.</summary>
        private readonly float capRoundness = 1f;

        /// <summary>How far a cap reaches past its end particle, in radii of that particle.</summary>
        private readonly float capOffset = 1f;

        // Where the system's lighting is sampled, or null to sample at the center of its bounds
        private readonly ITransformProvider? lightingTransform;
        private readonly Scene scene;
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
        private float[] lastAlphas = [];

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

            // The cable shader samples no normal map, so the normal map offsets (m_flNormalMapOffsetU and V) have
            // nothing to move.
            // TODO: m_MaterialFloatVars and m_MaterialVecVars set dynamic material attributes by attribute name,
            // which the cable material does not map to its parameters. What a vector variable leaves in w is not
            // known either.
            drawCableCaps = parse.Boolean("m_bDrawCableCaps", drawCableCaps);
            capRoundness = Math.Clamp(parse.Float("m_flCapRoundness", capRoundness), 0f, 2f);
            capOffset = Math.Clamp(parse.Float("m_flCapOffsetAmount", capOffset), 0.01f, 2f);
            this.scene = scene;

            var lighting = parse.TransformInput("m_LightingTransform", new IdentityTransformProvider());

            if (lighting is not IdentityTransformProvider)
            {
                lightingTransform = lighting;
            }

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

            var castsShadows = !material.DoNotCastShadows && material.IntParams.GetValueOrDefault("F_DO_NOT_CAST_SHADOWS") == 0;
            CanRenderDepth = Pass == RenderPass.Opaque && castsShadows && !OnlyRenderInEffectsWaterPass && material.Shader.DepthMode != null;
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
            UpdateLightingOrigin(systemState);

            var scales = EvaluateScales(systemState);

            if (particles.Count < 2 || scales.Radius == 0f || scales.Alpha == 0f)
            {
                indexCount = 0;
                lastCount = 0;
                return;
            }

            var count = particles.Count;
            var current = particles.Current;
            var positions = current.ReadVectors(ParticleField.Position);
            var particleColors = current.ReadVectors(ParticleField.Color);
            var particleRadii = current.ReadFloats(ParticleField.Radius);
            var particleAlphas = current.ReadFloats(ParticleField.Alpha);

            var radii = ParticleScratch.Floats(count);
            var colors = ParticleScratch.Vectors(count);
            var alphas = ParticleScratch.Floats(count);

            for (var i = 0; i < count; i++)
            {
                var color = ParticleColorBlend.Apply(ColorBlendType,
                    ParticleColorBlend.ToWrappedBytes(particleColors[i], Math.Clamp(scales.Alpha * 255f * particleAlphas[i], 0f, 255f)),
                    scales.Color) / 255f;

                radii[i] = MathF.Max(0f, scales.Radius * particleRadii[i]);
                colors[i] = color.AsVector3();
                alphas[i] = color.W;
            }

            using var levelsBuffer = new RentedBuffer<int>(count - 1);
            var levels = levelsBuffer.Span;
            ComputeTessellationLevels(positions, camera, levels);

            if (!GeometryChanged(positions, levels, radii, colors, alphas))
            {
                return;
            }

            lastCount = count;
            lastPositions = EnsureCapacity(lastPositions, count);
            lastLevels = EnsureCapacity(lastLevels, count - 1);
            lastRadii = EnsureCapacity(lastRadii, count);
            lastColors = EnsureCapacity(lastColors, count);
            lastAlphas = EnsureCapacity(lastAlphas, count);
            positions.CopyTo(lastPositions);
            levels.CopyTo(lastLevels);
            radii.CopyTo(lastRadii);
            colors.CopyTo(lastColors);
            alphas.CopyTo(lastAlphas);

            var repeatsPerSegment = textureRepeatsPerSegment.NextNumber(systemState);
            var circumference = circumferenceRepeats.NextNumber(systemState);

            // Only the fractional part of each offset is used, truncated toward zero.
            var colorMapOffset = new Vector2(colorMapOffsetU.NextNumber(systemState) % 1f, colorMapOffsetV.NextNumber(systemState) % 1f);

            // In PATH mode the authored repeat count is spread over the whole cable instead of per segment.
            // With caps the cable counts a segment more, for each cap, than the tube between its end particles.
            var pathSegments = drawCableCaps ? count + 1 : count - 1;
            var repeats = textureRepetitionMode == TextureRepetitionMode.TEXTURE_REPETITION_PATH
                ? repeatsPerSegment / pathSegments
                : repeatsPerSegment;

            // Exact sizes are known up front, so build buffers are rented and filled by index. Rented
            // arrays may be larger, so sizes are passed explicitly.
            var ringCount = TotalRings(positions.Length, levels) + CapRings(levels);
            var sides = CableMeshBuilder.SideCount(roundness);
            var vertexCount = ringCount * (sides + 1);
            var tubeIndexCount = (ringCount - 1) * sides * 6;

            using var ringPositions = new RentedBuffer<Vector3>(ringCount);
            using var ringSamples = new RentedBuffer<RopeSample>(ringCount);
            using var vertexBuffer = new RentedBuffer<CableVertex>(vertexCount);
            using var indexBuffer = new RentedBuffer<uint>(tubeIndexCount);

            BuildRings(positions, radii, colors, alphas, levels, repeats, ringPositions.Span, ringSamples.Span);

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

        /// <summary>
        /// Makes the system's node sample its lighting at the position of <c>m_LightingTransform</c>, when
        /// one is set.
        /// </summary>
        private void UpdateLightingOrigin(ParticleSystemState systemState)
        {
            if (lightingTransform == null || OwnerNode is not { } node)
            {
                return;
            }

            var origin = lightingTransform.NextTransform(systemState).Translation;

            if (node.LightingOrigin == origin)
            {
                return;
            }

            node.LightingOrigin = origin;
            scene.UpdateNodeEnvironmentMaps(node);
        }

        /// <inheritdoc/>
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
        /// Writes one level per segment into <paramref name="levels"/>.
        /// </summary>
        private void ComputeTessellationLevels(ReadOnlySpan<Vector3> positions, Camera camera, Span<int> levels)
        {
            var segmentCount = levels.Length;

            // Scaled against a 1920 reference resolution, in integer math.
            var resolutionScale = (int)camera.WindowSize.Y * 64 / 1920;

            var directions = ParticleScratch.Vectors(segmentCount);
            for (var i = 0; i < segmentCount; i++)
            {
                var direction = positions[i + 1] - positions[i];
                directions[i] = MathUtils.SafeNormalize(direction, Vector3.UnitX, ParticleMath.MinimumLengthSquared);
            }

            for (var i = 0; i < segmentCount; i++)
            {
                var boundsRadius = Vector3.Distance(positions[i], positions[i + 1]) * SegmentBoundsScale * 0.5f;
                var midpoint = (positions[i] + positions[i + 1]) * 0.5f;
                var size = ScreenSizeFraction(midpoint, boundsRadius, camera);

                // The ceiling is applied after the floor, so it wins when it is below the floor
                var tess = size * tessScale * resolutionScale;
                var subdivisions = Math.Clamp(Math.Min(Math.Max((int)tess, minTessellation), maxTessellation), 1, 1 << MaxTessellationLevel);
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
            for (var pass = 0; pass < MaxTessellationLevel && TotalRings(positions.Length, levels) + CapRings(levels) > MaxTubeRings; pass++)
            {
                for (var i = 0; i < segmentCount; i++)
                {
                    levels[i] = Math.Max(0, levels[i] - 1);
                }
            }
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

        /// <summary>The rings the end caps add, each tessellated as finely as the tube segment it closes, within 2 to 16.</summary>
        private int CapRings(ReadOnlySpan<int> levels)
            => drawCableCaps ? (1 << CapLevel(levels[0])) + (1 << CapLevel(levels[^1])) : 0;

        private static int CapLevel(int segmentLevel) => Math.Clamp(segmentLevel, 1, 4);

        // Expands each particle segment into 2^level rings. Position and radius follow a Catmull-Rom spline
        // through the particles, with the end particles repeated past both ends; colour and V are linear.
        // With caps, the start cap shares V with the first segment and the end cap takes V from the particle
        // count, one segment past the last particle.
        // Fills exactly TotalRings plus CapRings entries of the (pooled, possibly larger) output arrays.
        private void BuildRings(ReadOnlySpan<Vector3> positions, ReadOnlySpan<float> radii, ReadOnlySpan<Vector3> colors, ReadOnlySpan<float> alphas,
            ReadOnlySpan<int> levels, float repeats, Span<Vector3> ringPositions, Span<RopeSample> ringSamples)
        {
            var cursor = 0;
            var last = positions.Length - 1;

            if (drawCableCaps)
            {
                var outward = MathUtils.SafeNormalize(positions[0] - positions[1]);
                var cap = new CapShape(positions[0], outward, radii[0], capRoundness, capOffset);

                cursor = AddCapRings(cap, atStart: true, 1 << CapLevel(levels[0]), colors[0], alphas[0], 0f, repeats, ringPositions, ringSamples, cursor);
            }

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
                        CableMeshBuilder.CatmullRom(radii[before], radii[i], radii[i + 1], radii[after], t),
                        Vector3.Lerp(colors[i], colors[i + 1], t),
                        (i + t) * repeats, pinned: false, MathUtils.Lerp(alphas[i], alphas[i + 1], t));
                    cursor++;
                }
            }

            ringPositions[cursor] = positions[last];
            ringSamples[cursor] = new RopeSample(positions[last], radii[last], colors[last], last * repeats, pinned: false, alphas[last]);
            cursor++;

            if (drawCableCaps)
            {
                var outward = MathUtils.SafeNormalize(positions[last] - positions[last - 1]);
                var cap = new CapShape(positions[last], outward, radii[last], capRoundness, capOffset);

                AddCapRings(cap, atStart: false, 1 << CapLevel(levels[^1]), colors[last], alphas[last], positions.Length * repeats, repeats, ringPositions, ringSamples, cursor);
            }
        }

        /// <summary>How far past its end particle a cap's tip sits.</summary>
        private const float CapTipDistance = 0.01f;

        /// <summary>
        /// The four spline points one cap is shaped by, each with a radius: its tip, the rim it closes to, the end
        /// particle and a point back inside the tube. The tip only shapes the curve; its radius, negative when the
        /// cap is round at all, makes the cap swell faster the rounder it is and centres the dome the cap's normals
        /// form. An end particle level with its neighbour collapses its cap onto itself.
        /// </summary>
        private readonly struct CapShape(Vector3 end, Vector3 outward, float radius, float roundness, float offset)
        {
            public Vector3 End { get; } = end;

            public float Radius { get; } = radius;

            public Vector3 Tip { get; } = end + outward * CapTipDistance;

            public float TipRadius { get; } = -radius * roundness * 3f;

            public Vector3 Rim { get; } = end + outward * (radius * offset);

            public Vector3 Inner { get; } = end - outward * (radius * offset * roundness * 2f);
        }

        /// <summary>
        /// Adds the rings of a cap: one spline segment between the rim and the end particle, the start cap running
        /// up to the end particle without its ring, the end cap running on from it. Returns the cursor past them.
        /// </summary>
        private static int AddCapRings(in CapShape cap, bool atStart, int subdivisions, Vector3 color, float alpha, float firstV, float repeats,
            Span<Vector3> ringPositions, Span<RopeSample> ringSamples, int cursor)
        {
            var first = atStart ? 0 : 1;
            Vector3? domeCenter = cap.TipRadius < 0f ? cap.Tip : null;

            for (var s = first; s < first + subdivisions; s++)
            {
                var t = s / (float)subdivisions;
                Vector3 position;
                float radius;

                // The spline runs inward from the tip at the start and outward to it at the end
                if (atStart)
                {
                    position = CableMeshBuilder.CatmullRom(cap.Tip, cap.Rim, cap.End, cap.Inner, t);
                    radius = CableMeshBuilder.CatmullRom(cap.TipRadius, 0f, cap.Radius, 0f, t);
                }
                else
                {
                    position = CableMeshBuilder.CatmullRom(cap.Inner, cap.End, cap.Rim, cap.Tip, t);
                    radius = CableMeshBuilder.CatmullRom(0f, cap.Radius, 0f, cap.TipRadius, t);
                }

                ringPositions[cursor] = position;
                ringSamples[cursor] = new RopeSample(position, radius, color, firstV + t * repeats, pinned: false, alpha, domeCenter);
                cursor++;
            }

            return cursor;
        }

        private bool GeometryChanged(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> levels, ReadOnlySpan<float> radii, ReadOnlySpan<Vector3> colors,
            ReadOnlySpan<float> alphas)
        {
            // The count check guards the slices below: when it passes, lastCount >= 2.
            return positions.Length != lastCount
                || !levels.SequenceEqual(lastLevels.AsSpan(0, lastCount - 1))
                || !radii.SequenceEqual(lastRadii.AsSpan(0, lastCount))
                || !colors.SequenceEqual(lastColors.AsSpan(0, lastCount))
                || !alphas.SequenceEqual(lastAlphas.AsSpan(0, lastCount))
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

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => material.Shader.RenderModes;

        /// <inheritdoc/>
        public override void Delete()
        {
            VertexArray.Delete(vaoHandle);
            GL.DeleteBuffer(vertexBufferHandle);
            GL.DeleteBuffer(indexBufferHandle);
        }
    }
}
