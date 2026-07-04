using System.Buffers;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders the ordered particle chain as a lit round tube. The tube tessellation is shared with the
    /// map <c>path_particle_rope</c> path via <see cref="CableMeshBuilder"/>; geometry is rebuilt only when
    /// the particle positions or the per-segment tessellation levels change.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderCables">C_OP_RenderCables</seealso>
    internal class RenderCables : ParticleFunctionRenderer
    {
        private const string ShaderName = "vrf.particle_cable";

        private Shader shader;
        private readonly Scene scene;
        private readonly RenderMaterial material;
        private readonly int vaoHandle;
        private int vertexBufferHandle;
        private int indexBufferHandle;

        // The probe volume the cable's scene node is bound to, resolved on first draw because the
        // scene computes the bindings after all nodes are loaded.
        private SceneLightProbe? lightProbe;
        private bool lightProbeResolved;

        private const int MaxTessellationLevel = 7;
        private const int MaxTubeRings = 8192;

        private readonly int roundness = 1;
        private readonly TextureRepetitionMode textureRepetitionMode;
        private readonly INumberProvider textureRepeatsPerSegment = new LiteralNumberProvider(1f);
        private readonly INumberProvider circumferenceRepeats = new LiteralNumberProvider(1f);
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

        // The tube is re-tessellated whenever the positions or tessellation levels change (every frame
        // while the camera moves or the rope sways), so the transient ring/vertex/index buffers come from
        // pools shared across all cable instances. The index buffer gets its own pool because its worst
        // case exceeds ArrayPool<uint>.Shared's largest bucket (2^20 elements), which would otherwise turn
        // every rebuild of a max-size tube into a fresh multi-megabyte allocation.
        private static readonly ArrayPool<uint> IndexArrayPool = ArrayPool<uint>.Create(
            maxArrayLength: (MaxTubeRings - 1) * CableMeshBuilder.MaxSides * 6,
            maxArraysPerBucket: 2);

        private static readonly IComparer<(int Id, Vector3 Position, float Radius, Vector3 Color)> ChainComparer =
            Comparer<(int Id, Vector3 Position, float Radius, Vector3 Color)>.Create(static (a, b) => a.Id.CompareTo(b.Id));

        public RenderCables(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse)
        {
            this.scene = scene;

            shader = rendererContext.ShaderLoader.LoadShader(ShaderName);

            roundness = parse.Int32("m_nRoundness", roundness);
            textureRepetitionMode = parse.Enum("m_nTextureRepetitionMode", textureRepetitionMode);
            tessScale = parse.Float("m_flTessScale", tessScale);
            minTessellation = parse.Int32("m_nMinTesselation", minTessellation);
            // Guard against inverted authored bounds so the per-frame Math.Clamp cannot throw.
            maxTessellation = Math.Max(minTessellation, parse.Int32("m_nMaxTesselation", maxTessellation));
            textureRepeatsPerSegment = parse.NumberProvider("m_flTextureRepeatsPerSegment", textureRepeatsPerSegment);
            circumferenceRepeats = parse.NumberProvider("m_flTextureRepeatsCircumference", circumferenceRepeats);

            var materialName = parse.Data.ContainsKey("m_hMaterial") ? parse.Data.GetStringProperty("m_hMaterial") : null;
            material = materialName != null
                ? rendererContext.MaterialLoader.GetMaterial(materialName, null)
                : new RenderMaterial(shader);

            // A cable without an authored colour texture uses a default white one, showing the vertex
            // colour rather than the shader-default error checker.
            if (!material.Textures.ContainsKey("g_tColor"))
            {
                material.Textures["g_tColor"] = rendererContext.MaterialLoader.GetDefaultColor();
            }

            vaoHandle = SetupBuffers();
        }

        private int SetupBuffers()
        {
            GL.CreateVertexArrays(1, out int vao);
            GL.CreateBuffers(1, out vertexBufferHandle);
            GL.CreateBuffers(1, out indexBufferHandle);

            var stride = Marshal.SizeOf<CableMeshBuilder.Vertex>();
            GL.VertexArrayVertexBuffer(vao, 0, vertexBufferHandle, 0, stride);
            GL.VertexArrayElementBuffer(vao, indexBufferHandle);

            SetupAttrib(vao, "aVertexPosition", 3, VertexAttribType.Float, false, nameof(CableMeshBuilder.Vertex.Position));
            SetupAttrib(vao, "aVertexNormal", 3, VertexAttribType.Float, false, nameof(CableMeshBuilder.Vertex.Normal));
            SetupAttrib(vao, "aTexCoords", 2, VertexAttribType.Float, false, nameof(CableMeshBuilder.Vertex.UV));
            SetupAttrib(vao, "aVertexColor", 4, VertexAttribType.UnsignedByte, true, nameof(CableMeshBuilder.Vertex.Color));

            return vao;
        }

        private void SetupAttrib(int vao, string attribName, int size, VertexAttribType type, bool normalized, string field)
        {
            var location = GL.GetAttribLocation(shader.Program, attribName);
            if (location < 0)
            {
                return;
            }

            GL.EnableVertexArrayAttrib(vao, location);
            GL.VertexArrayAttribFormat(vao, location, size, type, normalized, (int)Marshal.OffsetOf<CableMeshBuilder.Vertex>(field));
            GL.VertexArrayAttribBinding(vao, location, 0);
        }

        public override void Render(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Camera camera)
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
                chain[index++] = (particle.ParticleID, particle.Position, particle.Radius, particle.Color);
            }

            chain.Sort(ChainComparer);

            if (!lightProbeResolved)
            {
                ResolveLightProbe(systemRenderState, chain[count / 2].Position);
            }

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

            var levels = ComputeTessellationLevels(positions, chain, camera);

            if (!GeometryChanged(positions, levels, radii, colors))
            {
                DrawTube();
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

            var repeatsPerSegment = textureRepeatsPerSegment.NextNumber(systemRenderState);
            if (repeatsPerSegment == 0f)
            {
                repeatsPerSegment = 1f;
            }

            var circumference = circumferenceRepeats.NextNumber(systemRenderState);
            if (circumference == 0f)
            {
                circumference = 1f;
            }

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

            var ringPositions = ArrayPool<Vector3>.Shared.Rent(ringCount);
            var ringSamples = ArrayPool<RopeSample>.Shared.Rent(ringCount);
            var vertexArray = ArrayPool<CableMeshBuilder.Vertex>.Shared.Rent(vertexCount);
            var indexArray = IndexArrayPool.Rent(tubeIndexCount);

            try
            {
                BuildRings(positions, chain, levels, repeats, ringPositions, ringSamples);

                if (!CableMeshBuilder.BuildTubeMesh(ringPositions.AsSpan(0, ringCount), ringSamples.AsSpan(0, ringCount),
                    sides, circumference, vertexArray.AsSpan(0, vertexCount), indexArray.AsSpan(0, tubeIndexCount)))
                {
                    indexCount = 0;
                    return;
                }

                var stride = Marshal.SizeOf<CableMeshBuilder.Vertex>();
                GL.NamedBufferData(vertexBufferHandle, vertexCount * stride, vertexArray, BufferUsageHint.DynamicDraw);
                GL.NamedBufferData(indexBufferHandle, tubeIndexCount * sizeof(uint), indexArray, BufferUsageHint.DynamicDraw);
                indexCount = tubeIndexCount;
            }
            finally
            {
                ArrayPool<Vector3>.Shared.Return(ringPositions);
                ArrayPool<RopeSample>.Shared.Return(ringSamples);
                ArrayPool<CableMeshBuilder.Vertex>.Shared.Return(vertexArray);
                IndexArrayPool.Return(indexArray);
            }

            DrawTube();
        }

        /// <summary>
        /// Per-segment length tessellation: the apparent on-screen radius scaled by m_flTessScale picks a
        /// power-of-two subdivision count within [m_nMinTesselation, m_nMaxTesselation], bumped one or two
        /// levels where adjacent segments bend sharply.
        /// </summary>
        private Span<int> ComputeTessellationLevels(ReadOnlySpan<Vector3> positions,
            ReadOnlySpan<(int Id, Vector3 Position, float Radius, Vector3 Color)> chain, Camera camera)
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
                directions[i] = direction.LengthSquared() > 1e-8f ? Vector3.Normalize(direction) : Vector3.UnitX;
            }

            for (var i = 0; i < segmentCount; i++)
            {
                var radius = chain[i].Radius;
                var midpoint = (positions[i] + positions[i + 1]) * 0.5f;
                var distanceSquared = (midpoint - camera.Location).LengthSquared();

                // Apparent radius as a fraction of the viewport; full when the camera is inside the tube.
                var size = distanceSquared <= radius * radius
                    ? 1f
                    : Math.Clamp(radius * camera.ProjectionMatrix.M22 / MathF.Sqrt(distanceSquared), 0f, 1f);

                var tess = size * tessScale * resolutionScale;
                var subdivisions = Math.Clamp(Math.Clamp((int)tess, minTessellation, maxTessellation), 1, 1 << MaxTessellationLevel);
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

        // Expands each particle segment into 2^level rings, interpolating position/radius/colour/U.
        // Fills exactly TotalRings entries of the (pooled, possibly larger) output arrays.
        private static void BuildRings(ReadOnlySpan<Vector3> positions, ReadOnlySpan<(int Id, Vector3 Position, float Radius, Vector3 Color)> chain,
            ReadOnlySpan<int> levels, float repeats, Vector3[] ringPositions, RopeSample[] ringSamples)
        {
            var cursor = 0;

            for (var i = 0; i < levels.Length; i++)
            {
                var subdivisions = 1 << levels[i];
                for (var s = 0; s < subdivisions; s++)
                {
                    var t = s / (float)subdivisions;
                    var position = Vector3.Lerp(positions[i], positions[i + 1], t);
                    ringPositions[cursor] = position;

                    // Particles are roughly evenly spaced, so index-based U tracks arc length.
                    ringSamples[cursor] = new RopeSample(position,
                        float.Lerp(chain[i].Radius, chain[i + 1].Radius, t),
                        Vector3.Lerp(chain[i].Color, chain[i + 1].Color, t),
                        (i + t) * repeats, false);
                    cursor++;
                }
            }

            var last = positions.Length - 1;
            ringPositions[cursor] = positions[last];
            ringSamples[cursor] = new RopeSample(positions[last], chain[last].Radius, chain[last].Color, last * repeats, false);
        }

        // Prefers the probe volume containing the cable midpoint, falling back to the binding the
        // scene assigned to the owning node. When one exists the shader is swapped for the
        // probe-sampling variant (same vertex layout, so the VAO is kept).
        private void ResolveLightProbe(ParticleSystemRenderState systemRenderState, Vector3 cablePosition)
        {
            lightProbeResolved = true;

            if (!scene.LightingInfo.HasValidLightProbes)
            {
                return;
            }

            lightProbe = FindContainingProbe(cablePosition) ?? systemRenderState.OwnerNode?.LightProbeBinding;
            if (lightProbe?.Irradiance == null)
            {
                lightProbe = null;
                return;
            }

            var arguments = new Dictionary<string, byte>(scene.RenderAttributes)
            {
                ["D_BAKED_LIGHTING_FROM_PROBE"] = 1,
            };

            shader = scene.RendererContext.ShaderLoader.LoadShader(ShaderName, arguments);
        }

        // Highest-priority (then smallest) complete probe volume containing the given position,
        // matching the ordering the scene uses when binding probes to nodes.
        private SceneLightProbe? FindContainingProbe(Vector3 position)
        {
            var isAtlas = scene.LightingInfo.LightProbeType == LightProbeType.ProbeAtlas;
            SceneLightProbe? best = null;

            foreach (var probe in scene.LightingInfo.LightProbes)
            {
                if (probe.Irradiance == null || (isAtlas && probe.DirectLightShadows == null) || !probe.BoundingBox.Contains(position))
                {
                    continue;
                }

                if (best == null
                    || probe.IndoorOutdoorLevel > best.IndoorOutdoorLevel
                    || (probe.IndoorOutdoorLevel == best.IndoorOutdoorLevel && probe.AtlasSize.LengthSquared() < best.AtlasSize.LengthSquared()))
                {
                    best = probe;
                }
            }

            return best;
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

        private void DrawTube()
        {
            if (indexCount == 0)
            {
                return;
            }

            shader.Use();
            GL.BindVertexArray(vaoHandle);
            material.Render(shader);

            // todo: batch tube draws and call this less often
            scene.LightingInfo.SetLightmapTextures(shader);

            if (lightProbe is not null)
            {
                shader.SetUniform1("uLightProbeIndex", (uint)lightProbe.ShaderIndex);
                scene.LightingInfo.SetInstanceLightProbeTextures(shader, lightProbe);
            }

            // The tube is opaque geometry drawn in the translucent pass: disable blending and write depth so
            // it self-occludes correctly, then restore the translucent defaults.
            GL.Disable(EnableCap.Blend);
            GL.DepthMask(true);

            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);

            material.PostRender();
            GL.DepthMask(false);
            GL.Enable(EnableCap.Blend);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void Delete()
        {
            GL.DeleteVertexArray(vaoHandle);
            GL.DeleteBuffer(vertexBufferHandle);
            GL.DeleteBuffer(indexBufferHandle);
        }
    }
}
