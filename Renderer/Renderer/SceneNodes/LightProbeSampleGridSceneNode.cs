using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// The samples of a light probe volume as points, which the shader grows into spheres or cubes and
    /// lights from the volume like any other surface.
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

        private readonly SceneLightProbe probe;

        private Shader shader;
        private bool cubes;

        // Lit from its own volume, whichever is closer to it
        internal override SceneLightProbe? OwnLightProbe => probe;

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

        /// <summary>Gets or sets whether samples are drawn as cubes rather than spheres.</summary>
        public bool Cubes
        {
            get => cubes;
            set
            {
                if (cubes != value)
                {
                    cubes = value;
                    shader = LoadShader();
                }
            }
        }

        public LightProbeSampleGridSceneNode(Scene scene, SceneLightProbe probe, ReadOnlySpan<Vector3> localSamples)
            : base(scene)
        {
            this.probe = probe;

            sampleBounds = probe.LocalBoundingBox;
            Transform = probe.Transform;
            SampleSize = 4f;

            sampleCount = localSamples.Length;
            shader = LoadShader();

            vertexBuffer = new StreamingVertexBuffer(nameof(LightProbeSampleGridSceneNode));
            vao = SamplePoint.InputLayout.CreateVertexArray(nameof(LightProbeSampleGridSceneNode), vertexBuffer.Handle);
            vertexBuffer.AttachTo(vao, SamplePoint.InputLayout.Stride);
            vertexBuffer.Upload(MemoryMarshal.AsBytes(localSamples));
        }

        private Shader LoadShader()
        {
            var arguments = Scene.LightingInfo.CreateShaderArguments();
            arguments["D_POINT_TO_CUBE"] = (byte)(cubes ? 1 : 0);

            return Scene.RendererContext.ShaderLoader.LoadShader("lpv_debug_grid", arguments);
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            // Points have nothing for depth, picking or outline shaders to draw
            if (context.RenderPass != RenderPass.Opaque || context.ReplacementShader != null)
            {
                return;
            }

            shader.Use();
            shader.SetUniform("g_flPointSize", SampleSize);
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

            GL.DrawArraysInstancedBaseInstance(PrimitiveType.Points, 0, sampleCount, 1, Id);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            VertexArray.Delete(vao);
            vertexBuffer.Delete();
        }
    }
}
