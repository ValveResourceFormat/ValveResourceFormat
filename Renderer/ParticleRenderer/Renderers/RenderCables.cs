using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
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
        private readonly RenderTexture texture;
        private readonly float roughness = 1f;
        private readonly float diffuseAmount = 1f;
        private readonly float selfIllumAmount;
        private readonly int vaoHandle;
        private int vertexBufferHandle;
        private int indexBufferHandle;

        // The probe volume the cable's scene node is bound to, resolved on first draw because the
        // scene computes the bindings after all nodes are loaded.
        private SceneLightProbe? lightProbe;
        private bool lightProbeResolved;
        private RenderTexture? sunShadowDepth;

        private const int MaxTessellationLevel = 7;
        private const int MaxTubeRings = 8192;

        private readonly int roundness = 1;
        private readonly TextureRepetitionMode textureRepetitionMode;
        private readonly INumberProvider textureRepeatsPerSegment = new LiteralNumberProvider(1f);
        private readonly INumberProvider circumferenceRepeats = new LiteralNumberProvider(1f);
        private readonly float tessScale = 1f;
        private readonly int minTessellation = 1;
        private readonly int maxTessellation = 128;

        // Cached tube so a settled/static cable is not re-tessellated every frame.
        private int indexCount;
        private Vector3[] lastPositions = [];
        private int[] lastLevels = [];
        private float[] lastRadii = [];
        private Vector3[] lastColors = [];

        // Per-frame scratch reused across frames so a settled cable allocates nothing; each is sized to the
        // live particle (or segment) count and only reallocated when that count changes.
        private (int Id, Vector3 Position, float Radius, Vector3 Color)[] chainScratch = [];
        private Vector3[] positionsScratch = [];
        private float[] radiiScratch = [];
        private Vector3[] colorsScratch = [];
        private int[] levelsScratch = [];
        private Vector3[] directionsScratch = [];

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
            var material = materialName != null ? rendererContext.MaterialLoader.GetMaterial(materialName, null) : null;
            // A cable without a material uses a default white texture; show the vertex colour rather
            // than the error checker.
            texture = material?.Textures.GetValueOrDefault("g_tColor") ?? rendererContext.MaterialLoader.GetDefaultColor();
            roughness = material?.Material.FloatParams.GetValueOrDefault("g_flRoughness", roughness) ?? roughness;
            diffuseAmount = material?.Material.FloatParams.GetValueOrDefault("g_flDiffuseAmount", diffuseAmount) ?? diffuseAmount;
            selfIllumAmount = material?.Material.FloatParams.GetValueOrDefault("g_flSelfIllumAmount", selfIllumAmount) ?? selfIllumAmount;

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
                lastPositions = [];
                lastLevels = [];
                lastRadii = [];
                lastColors = [];
                return;
            }

            // Order the live particles along the rope by particle id; prune compaction can reshuffle slots.
            var count = particles.Count;
            chainScratch = EnsureSize(chainScratch, count);
            var chain = chainScratch;
            var index = 0;
            foreach (ref var particle in particles.Current)
            {
                chain[index++] = (particle.ParticleID, particle.Position, particle.Radius, particle.Color);
            }

            Array.Sort(chain, ChainComparer);

            if (!lightProbeResolved)
            {
                ResolveLightProbe(systemRenderState, chain[chain.Length / 2].Position);
            }

            sunShadowDepth = systemRenderState.SunShadowDepth;

            positionsScratch = EnsureSize(positionsScratch, count);
            radiiScratch = EnsureSize(radiiScratch, count);
            colorsScratch = EnsureSize(colorsScratch, count);
            var positions = positionsScratch;
            var radii = radiiScratch;
            var colors = colorsScratch;
            for (var i = 0; i < chain.Length; i++)
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

            lastPositions = CopyInto(lastPositions, positions);
            lastLevels = CopyInto(lastLevels, levels);
            lastRadii = CopyInto(lastRadii, radii);
            lastColors = CopyInto(lastColors, colors);

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

            BuildRings(positions, chain, levels, repeats, out var ringPositions, out var ringSamples);

            var vertices = new List<CableMeshBuilder.Vertex>();
            var indices = new List<uint>();

            if (!CableMeshBuilder.BuildTubeMesh(ringPositions, ringSamples, roundness, circumference, vertices, indices))
            {
                indexCount = 0;
                return;
            }

            var stride = Marshal.SizeOf<CableMeshBuilder.Vertex>();
            var vertexArray = vertices.ToArray();
            var indexArray = indices.ToArray();
            GL.NamedBufferData(vertexBufferHandle, vertexArray.Length * stride, vertexArray, BufferUsageHint.DynamicDraw);
            GL.NamedBufferData(indexBufferHandle, indexArray.Length * sizeof(uint), indexArray, BufferUsageHint.DynamicDraw);
            indexCount = indexArray.Length;

            DrawTube();
        }

        /// <summary>
        /// Per-segment length tessellation: the apparent on-screen radius scaled by m_flTessScale picks a
        /// power-of-two subdivision count within [m_nMinTesselation, m_nMaxTesselation], bumped one or two
        /// levels where adjacent segments bend sharply.
        /// </summary>
        private int[] ComputeTessellationLevels(Vector3[] positions, (int Id, Vector3 Position, float Radius, Vector3 Color)[] chain, Camera camera)
        {
            var segmentCount = positions.Length - 1;
            levelsScratch = EnsureSize(levelsScratch, segmentCount);
            var levels = levelsScratch;

            // Scaled against a 1920 reference resolution, in integer math.
            var resolutionScale = (int)camera.WindowSize.Y * 64 / 1920;

            directionsScratch = EnsureSize(directionsScratch, segmentCount);
            var directions = directionsScratch;
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

        private static int TotalRings(int nodeCount, int[] levels)
        {
            var total = nodeCount;
            foreach (var level in levels)
            {
                total += (1 << level) - 1;
            }

            return total;
        }

        // Expands each particle segment into 2^level rings, interpolating position/radius/colour/U.
        private static void BuildRings(Vector3[] positions, (int Id, Vector3 Position, float Radius, Vector3 Color)[] chain,
            int[] levels, float repeats, out List<Vector3> ringPositions, out List<RopeSample> ringSamples)
        {
            var total = TotalRings(positions.Length, levels);
            ringPositions = new List<Vector3>(total);
            ringSamples = new List<RopeSample>(total);

            for (var i = 0; i < levels.Length; i++)
            {
                var subdivisions = 1 << levels[i];
                for (var s = 0; s < subdivisions; s++)
                {
                    var t = s / (float)subdivisions;
                    var position = Vector3.Lerp(positions[i], positions[i + 1], t);
                    ringPositions.Add(position);

                    // Particles are roughly evenly spaced, so index-based U tracks arc length.
                    ringSamples.Add(new RopeSample(position,
                        float.Lerp(chain[i].Radius, chain[i + 1].Radius, t),
                        Vector3.Lerp(chain[i].Color, chain[i + 1].Color, t),
                        (i + t) * repeats, false));
                }
            }

            var last = positions.Length - 1;
            ringPositions.Add(positions[last]);
            ringSamples.Add(new RopeSample(positions[last], chain[last].Radius, chain[last].Color, last * repeats, false));
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

        private bool GeometryChanged(Vector3[] positions, int[] levels, float[] radii, Vector3[] colors)
        {
            if (positions.Length != lastPositions.Length || !levels.AsSpan().SequenceEqual(lastLevels)
                || !radii.AsSpan().SequenceEqual(lastRadii) || !colors.AsSpan().SequenceEqual(lastColors))
            {
                return true;
            }

            for (var i = 0; i < positions.Length; i++)
            {
                if (positions[i] != lastPositions[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static T[] EnsureSize<T>(T[] buffer, int size) => buffer.Length == size ? buffer : new T[size];

        private static T[] CopyInto<T>(T[] target, T[] source)
        {
            if (target.Length != source.Length)
            {
                target = new T[source.Length];
            }

            Array.Copy(source, target, source.Length);
            return target;
        }

        private void DrawTube()
        {
            if (indexCount == 0)
            {
                return;
            }

            shader.Use();
            GL.BindVertexArray(vaoHandle);
            shader.SetTexture(0, "g_tColor", texture);
            shader.SetUniform1("g_flRoughness", roughness);
            shader.SetUniform1("g_flDiffuseAmount", diffuseAmount);
            shader.SetUniform1("g_flSelfIllumAmount", selfIllumAmount);

            if (lightProbe?.Irradiance is { } irradiance)
            {
                shader.SetUniform1("uLightProbeIndex", (uint)lightProbe.ShaderIndex);
                shader.SetTexture((int)ReservedTextureSlots.Probe1, "g_tLPV_Irradiance", irradiance);

                // Baked sun-visibility inputs; the lightmap version picks which set the shader variant declares.
                if (scene.LightingInfo.LightmapGameVersionNumber == 1)
                {
                    shader.SetTexture((int)ReservedTextureSlots.Probe2, "g_tLPV_Indices", lightProbe.DirectLightIndices);
                    shader.SetTexture((int)ReservedTextureSlots.Probe3, "g_tLPV_Scalars", lightProbe.DirectLightScalars);
                }
                else if (scene.LightingInfo.LightmapGameVersionNumber >= 2)
                {
                    shader.SetTexture((int)ReservedTextureSlots.Probe2, "g_tLPV_Shadows", lightProbe.DirectLightShadows);
                }
            }

            // Dynamic sun shadow map (bound per frame by the owning scene node); lets moving casters shadow the cable.
            if (sunShadowDepth is { } sunShadow)
            {
                shader.SetTexture((int)ReservedTextureSlots.ShadowDepthBufferDepth, "g_tShadowDepthBufferDepth", sunShadow);
            }

            // Cubemap fog samples a reserved-slot texture the mesh pass binds per shader; this pass must
            // bind it itself. Gradient fog needs no texture.
            var fogCube = scene.FogInfo.CubemapFog?.CubemapFogTexture;
            if (fogCube != null)
            {
                shader.SetTexture((int)ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", fogCube);
            }

            // The tube is opaque geometry drawn in the translucent pass: disable blending and write depth so
            // it self-occludes correctly, then restore the translucent defaults.
            GL.Disable(EnableCap.Blend);
            GL.DepthMask(true);

            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);

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
