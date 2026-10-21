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

            // Added after the scene assigned environment maps to its nodes, so the grid picks its own
            Scene.UpdateNodeEnvironmentMaps(this);

            sampleCount = localSamples.Length;
            shader = Scene.RendererContext.ShaderLoader.LoadShader("lpv_debug_grid", Scene.LightingInfo.CreateShaderArguments());

            // One sample per instance, whose quad the shader builds
            vertexBuffer = new StreamingVertexBuffer(nameof(LightProbeSampleGridSceneNode));
            vao = SamplePoint.InputLayout.CreateVertexArray(nameof(LightProbeSampleGridSceneNode), vertexBuffer.Handle, indexBuffer: 0, instanceDivisor: 1);
            vertexBuffer.AttachTo(vao, SamplePoint.InputLayout.Stride);
            vertexBuffer.Upload(MemoryMarshal.AsBytes(localSamples));
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
