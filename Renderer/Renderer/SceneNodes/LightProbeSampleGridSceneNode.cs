using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// The samples of a light probe volume as quads facing the view, in each of which the shader traces a
    /// sphere and lights it from the volume like any other surface.
    /// </summary>
    internal sealed class LightProbeSampleGridSceneNode : SceneNode
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SamplePoint
        {
            [VertexAttribute(VertexSlot.Position)] public Vector3 Position;

            public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<SamplePoint>();
        }

        private readonly StreamingVertexBuffer vertexBuffer;
        private readonly int vao;
        private readonly int sampleCount;
        private readonly AABB sampleBounds;

        private readonly Shader shader;

        /// <summary>Gets or sets the radius of a sample, in world units.</summary>
        public float SampleSize
        {
            get;
            set
            {
                field = value;
                LocalBoundingBox = new AABB(sampleBounds.Min - new Vector3(value), sampleBounds.Max + new Vector3(value));
            }
        }

        /// <summary>Gets or sets the surface colour of a sample, in sRGB.</summary>
        public Color32 Albedo { get; set; }

        /// <summary>Gets or sets the roughness of a sample.</summary>
        public float Roughness { get; set; }

        /// <summary>Gets or sets the metalness of a sample.</summary>
        public float Metalness { get; set; }

        public LightProbeSampleGridSceneNode(Scene scene, SceneLightProbe probe, ReadOnlySpan<Vector3> localSamples)
            : base(scene)
        {
            LightProbeBinding = probe;
            LightProbeVolumePrecomputedHandshake = probe.LightProbeVolumePrecomputedHandshake;

            sampleBounds = probe.LocalBoundingBox;
            Transform = probe.Transform;
            SampleSize = 4f;

            AssignEnvMaps();

            sampleCount = localSamples.Length;
            shader = Scene.RendererContext.ShaderLoader.LoadShader("lpv_debug_grid", Scene.LightingInfo.CreateShaderArguments());

            // One sample per instance, whose quad the shader builds
            vertexBuffer = new StreamingVertexBuffer(nameof(LightProbeSampleGridSceneNode));
            vao = SamplePoint.InputLayout.CreateVertexArray(nameof(LightProbeSampleGridSceneNode), vertexBuffer.Handle, indexBuffer: 0, instanceDivisor: 1);
            vertexBuffer.AttachTo(vao, SamplePoint.InputLayout.Stride);
            vertexBuffer.Upload(MemoryMarshal.AsBytes(localSamples));
        }

        // Added after the scene assigned environment maps to its nodes, so the grid picks its own: every one
        // reaching into the volume, in the order the scene ranks them for a node
        private void AssignEnvMaps()
        {
            var center = BoundingBox.Center;

            foreach (var envMap in Scene.LightingInfo.EnvMaps)
            {
                if (envMap.BoundingBox.Intersects(BoundingBox))
                {
                    EnvMaps.Add(envMap);
                }
            }

            EnvMaps.Sort((a, b) =>
            {
                var priority = b.IndoorOutdoorLevel.CompareTo(a.IndoorOutdoorLevel);

                return priority != 0
                    ? priority
                    : Vector3.Distance(center, a.BoundingBox.Center).CompareTo(Vector3.Distance(center, b.BoundingBox.Center));
            });

            ShaderEnvMapVisibility = default(SceneEnvMap.EnvMapVisibility128).Store(EnvMaps);
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            // The quads have nothing for depth, picking or outline shaders to draw
            if (context.RenderPass != RenderPass.Opaque || context.ReplacementShader != null)
            {
                return;
            }

            shader.Use();
            shader.SetUniform1("uObjectIndex", Id);
            shader.SetUniform("g_flSampleSize", SampleSize);
            shader.SetUniform("g_flNearPlane", context.Camera.NearPlane);
            shader.SetUniform("g_vAlbedo", Albedo.ToLinearColor().AsVector3());
            shader.SetUniform("g_flRoughness", Roughness);
            shader.SetUniform("g_flMetalness", Metalness);

            // What the mesh renderer binds per draw when the scene has no texture arrays for these
            var lightingInfo = Scene.LightingInfo;

            if (lightingInfo.CubemapType == CubemapType.IndividualCubemaps && EnvMaps.Count > 0)
            {
                GL.BindTextureUnit((int)ReservedTextureSlots.EnvironmentMap, EnvMaps[0].EnvMapTexture.Handle);
            }

            if (lightingInfo.LightProbeType == LightProbeType.IndividualProbes && LightProbeBinding is { } probe)
            {
                lightingInfo.BindInstanceLightProbeTextures(probe);
            }

            VertexArray.Bind(vao, shader);

            var renderState = GraphicsContext.RenderState;
            var state = renderState.CurrentPass;
            state.Rasterizer.CullMode = RsCullMode.None;

            using var _ = renderState.Scope(in state);

            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, sampleCount);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            VertexArray.Delete(vao);
            vertexBuffer.Delete();
        }
    }
}
