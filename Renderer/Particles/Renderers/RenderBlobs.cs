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
        private readonly RenderMaterial? material;
        private readonly float texCoordScale;

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

        public RenderBlobs(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse, scene)
        {
            this.scene = scene;
            this.rendererContext = rendererContext;

            cubeWidth = parse.NumberProvider("m_cubeWidth", cubeWidth);
            cutoffRadius = parse.NumberProvider("m_cutoffRadius", cutoffRadius);
            renderRadius = parse.NumberProvider("m_renderRadius", renderRadius);
            scaleControlPoint = Math.Clamp(parse.Int32("m_nScaleCP", scaleControlPoint), -1, 63);

            var materialName = parse.String("m_hMaterial");

            if (!string.IsNullOrEmpty(materialName))
            {
                material = rendererContext.MaterialLoader.GetMaterial(materialName, scene.LightingInfo.CreateShaderArguments());
                texCoordScale = material.Material.FloatParams.GetValueOrDefault("g_flTexCoordScale");

                // The mesh is only created once particles exist, by which point the loader may have closed
                // the material resource, so read its input signature while it is still open
                _ = material.Material.InputSignature;
            }
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

            ref var first = ref particles.Current[0];
            var width = cubeWidth.NextNumber(ref first, systemState);
            var cutoff = cutoffRadius.NextNumber(ref first, systemState);
            var radius = renderRadius.NextNumber(ref first, systemState);

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

            GL.NamedBufferData(vertexBuffer, vertices.Length * MetaballVertex.InputLayout.Stride, ref MemoryMarshal.GetReference(vertices), BufferUsageHint.DynamicDraw);
            GL.NamedBufferData(indexBuffer, indices.Length * sizeof(uint), ref MemoryMarshal.GetReference(indices), BufferUsageHint.DynamicDraw);

            drawCall!.VertexCount = (uint)vertices.Length;
            drawCall.IndexCount = indices.Length;
            node!.LocalBoundingBox = mesher.Bounds;
            node.Visible = true;
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
            // The surface draws as a mesh node with the rest of the scene
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
        }
    }
}
