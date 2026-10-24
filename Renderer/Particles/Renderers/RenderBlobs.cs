using System.Linq;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders the particles as one metaball surface drawn with the renderer's material: every particle
    /// is a centre of influence out to the cutoff radius, and the surface sits where an isolated particle
    /// would give a sphere of the render radius, merging where particles come close. The surface is a
    /// mesh scene node rebuilt each step, so it lights, shadows and sorts like any other mesh.
    /// </summary>
    /// <remarks>
    /// Only particle positions shape the surface; size, colour and alpha are not used. Material variables
    /// are not applied. The surface carries no texture coordinates of its own; materials that project their
    /// textures in world space by <c>g_flTexCoordScale</c> get those coordinates, all others sample one texel.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderBlobs">C_OP_RenderBlobs</seealso>
    internal class RenderBlobs : ParticleFunctionRenderer
    {
        private readonly Scene scene;
        private readonly RendererContext rendererContext;
        private RenderMaterial? material;
        private float texCoordScale;

        private readonly INumberProvider cubeWidth = new LiteralNumberProvider(1f);
        private readonly INumberProvider cutoffRadius = new LiteralNumberProvider(3.3f);
        private readonly INumberProvider renderRadius = new LiteralNumberProvider(1.3f);
        private readonly int scaleControlPoint = -1;

        private readonly MetaballMesher mesher = new();
        private Vector3[] centers = [];

        private MeshSceneNode? node;
        private DrawCall? drawCall;
        private int vertexBuffer;
        private int indexBuffer;
        private int vertexBufferCapacity;
        private int indexBufferCapacity;

        public RenderBlobs(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse, scene)
        {
            this.scene = scene;
            this.rendererContext = rendererContext;

            cubeWidth = parse.NumberProvider("m_cubeWidth", cubeWidth);
            cutoffRadius = parse.NumberProvider("m_cutoffRadius", cutoffRadius);
            renderRadius = parse.NumberProvider("m_renderRadius", renderRadius);
            scaleControlPoint = Math.Clamp(parse.Int32("m_nScaleCP", scaleControlPoint), -1, ParticleSystemState.MaxControlPoint);

            var materialName = parse.String("m_hMaterial");

            if (!string.IsNullOrEmpty(materialName))
            {
                UseMaterial(rendererContext.MaterialLoader.GetMaterial(materialName, scene.LightingInfo.CreateShaderArguments()));
            }
        }

        /// <inheritdoc/>
        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(scaleControlPoint);
        }

        /// <inheritdoc/>
        public override void SetMaterialOverride(RenderMaterial material)
        {
            Delete();
            UseMaterial(material);
        }

        private void UseMaterial(RenderMaterial newMaterial)
        {
            material = newMaterial;
            texCoordScale = material.Material.FloatParams.GetValueOrDefault("g_flTexCoordScale");
        }

        /// <inheritdoc/>
        public override bool DrawsInPasses => false;

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (material == null || particles.Count == 0)
            {
                Hide();
                return;
            }

            var first = particles.Current[0];
            var width = cubeWidth.NextNumber(first, systemState);
            var cutoff = cutoffRadius.NextNumber(first, systemState);
            var radius = renderRadius.NextNumber(first, systemState);

            if (scaleControlPoint >= 0)
            {
                var scale = systemState.GetControlPoint(scaleControlPoint).Position;
                width *= scale.X;
                cutoff *= scale.Y;
                radius *= scale.Z;
            }

            radius = MathF.Min(radius, cutoff * 0.999f);

            if (cutoff < 0.01f || radius < 0.01f)
            {
                Hide();
                return;
            }

            var blended = GetBlendedParticles(particles, systemState);

            if (centers.Length < blended.Length)
            {
                centers = new Vector3[particles.Capacity];
            }

            for (var i = 0; i < blended.Length; i++)
            {
                centers[i] = BiasTowardCamera(blended[i].Position, systemState.CameraPosition);
            }

            mesher.Build(centers.AsSpan(0, blended.Length), width, cutoff, radius, texCoordScale);

            if (mesher.Indices.Count == 0)
            {
                Hide();
                return;
            }

            Upload();
        }

        private void Upload()
        {
            if (node == null)
            {
                CreateNode();
            }

            var vertices = CollectionsMarshal.AsSpan(mesher.Vertices);
            var indices = CollectionsMarshal.AsSpan(mesher.Indices);

            UploadBuffer(vertexBuffer, ref vertexBufferCapacity, MemoryMarshal.AsBytes(vertices));
            UploadBuffer(indexBuffer, ref indexBufferCapacity, MemoryMarshal.AsBytes(indices));

            drawCall!.VertexCount = (uint)vertices.Length;
            drawCall.IndexCount = indices.Length;
            node!.LocalBoundingBox = mesher.Bounds;
            node.Visible = true;
        }

        /// <summary>Writes <paramref name="data"/> to the start of a buffer, growing its storage only when it does not fit.</summary>
        private static void UploadBuffer(int buffer, ref int capacity, ReadOnlySpan<byte> data)
        {
            if (data.Length > capacity)
            {
                capacity = (int)BitOperations.RoundUpToPowerOf2((uint)data.Length);
                GL.NamedBufferData(buffer, capacity, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }

            GL.NamedBufferSubData(buffer, IntPtr.Zero, data.Length, ref MemoryMarshal.GetReference(data));
        }

        private void CreateNode()
        {
            var name = $"{nameof(RenderBlobs)}: {material!.Material.Name}";

            vertexBuffer = GraphicsDevice.CreateBuffer($"{name} VB");
            indexBuffer = GraphicsDevice.CreateBuffer($"{name} IB");

            var mesh = RenderableMesh.CreateMesh(name, material, new VertexDrawBuffer
            {
                Handle = vertexBuffer,
                ElementSizeInBytes = (uint)MetaballVertex.InputLayout.Stride,
                InputLayoutFields = MetaballVertex.InputLayout.Fields(),
            }, indexBuffer, rendererContext);

            drawCall = mesh.DrawCalls.First();
            node = new MeshSceneNode(scene, mesh)
            {
                Name = name,
            };

            scene.Add(node, true);
        }

        /// <inheritdoc/>
        public override void Hide()
        {
            if (node != null)
            {
                node.Visible = false;
            }
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            if (node == null)
            {
                return;
            }

            if (scene.Remove(node, true))
            {
                node.Delete();
            }

            rendererContext.MeshBufferCache.InvalidateVertexArrayObjectsForFreedBuffers(vertexBuffer, indexBuffer);
            GL.DeleteBuffer(vertexBuffer);
            GL.DeleteBuffer(indexBuffer);

            node = null;
            drawCall = null;
            vertexBufferCapacity = 0;
            indexBufferCapacity = 0;
        }
    }
}
