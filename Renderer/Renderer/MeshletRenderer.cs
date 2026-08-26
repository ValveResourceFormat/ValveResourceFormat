using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Draws meshlet carrying geometry through a mesh shader instead of the vertex and index pipeline.
/// </summary>
///
/// <remarks>
/// One workgroup runs per meshlet. It reads the meshlet's slice of the MSLT block, fetches the vertices its
/// vertex list names straight out of the vertex buffer, and decodes the packed 6-bit references into
/// triangles, so neither a vertex array nor the index buffer takes part. A draw call whose mesh has no
/// meshlets has nothing to dispatch and is skipped.
/// </remarks>
public class MeshletRenderer(RendererContext rendererContext)
{
    private Shader? shader;

    /// <summary>Gets the mesh shader program, loading it on first use.</summary>
    /// <exception cref="InvalidOperationException">The driver has no mesh shader support.</exception>
    public Shader Shader => shader ??= LoadShader();

    /// <summary>Gets whether the current render mode has activated mesh shading.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets whether this renderer can draw at all, which needs a driver that runs mesh shaders.</summary>
    public static bool IsSupported => GLEnvironment.MeshShaderSupported;

    private Shader LoadShader()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException("Mesh shaders are not supported by this driver.");
        }

        return rendererContext.ShaderLoader.LoadShader("meshlet");
    }

    /// <summary>Loads the shader up front so its render mode is offered in the mode dropdown. Does nothing
    /// when the driver has no mesh shader support, which leaves the mode unlisted.</summary>
    public void Load()
    {
        if (IsSupported)
        {
            _ = Shader;
        }
    }

    /// <summary>Updates <see cref="IsActive"/> from the active render mode name.</summary>
    /// <param name="renderMode">The render mode the viewer switched to.</param>
    public void SetRenderMode(string renderMode)
    {
        IsActive = IsSupported && Shader.RenderModes.Contains(renderMode);
    }

    private ref struct Uniforms
    {
        public int Transform;
        public int FirstMeshlet;
        public int MeshletCount;
        public int BaseInstance;
        public int IsInstancing;
        public int BaseVertex;
        public int VertexStride;
        public int Position;
        public int Normal;
        public int TexCoord;

        public Uniforms() { }
    }

    /// <summary>Dispatches one mesh shader workgroup per meshlet for every request that has meshlets.</summary>
    /// <param name="requests">Draw call requests the pass collected.</param>
    /// <param name="context">Render context describing the current pass.</param>
    /// <param name="shader">The mesh shader program to draw with.</param>
    internal static void DrawBatch(List<MeshBatchRenderer.Request> requests, Scene.RenderContext context, Shader shader)
    {
        // Nothing here applies material state, so the pass baseline is latched for the whole batch. The
        // prepass laid this geometry down through the vertex pipeline and the mesh shader will not reproduce
        // that depth to the bit, so the equality test the prepassed pass sets up is relaxed.
        using var scope = GraphicsContext.RenderState.Scope(depthWrite: true, depthFunc: RsComparison.CloserEqual);

        shader.Use();

        // A mesh shader draw sources no attributes, but a core profile draw still needs some vertex array
        GL.BindVertexArray(context.Scene.RendererContext.MeshBufferCache.EmptyVAO);

        Debug.Assert(context.Scene.InstanceBufferGpu != null && context.Scene.TransformBufferGpu != null);
        context.Scene.InstanceBufferGpu.BindBufferBase();
        context.Scene.TransformBufferGpu.BindBufferBase();

        var uniforms = new Uniforms
        {
            Transform = shader.GetUniformLocation("transform"),
            FirstMeshlet = shader.GetUniformLocation("nFirstMeshlet"),
            MeshletCount = shader.GetUniformLocation("nMeshletCount"),
            BaseInstance = shader.GetUniformLocation("nBaseInstance"),
            IsInstancing = shader.GetUniformLocation("bIsInstancing"),
            BaseVertex = shader.GetUniformLocation("nBaseVertex"),
            VertexStride = shader.GetUniformLocation("nVertexStrideBytes"),
            Position = shader.GetUniformLocation("vPositionAttribute"),
            Normal = shader.GetUniformLocation("vNormalAttribute"),
            TexCoord = shader.GetUniformLocation("vTexCoordAttribute"),
        };

        var counters = PerfStats.Active;

        foreach (var request in requests)
        {
            if (request.Call is not { NumMeshlets: > 0 } drawCall
                || request.Mesh.MeshletBuffers is not { } meshletBuffers
                || drawCall.VertexBuffers.Length == 0)
            {
                continue;
            }

            var vertexBuffer = drawCall.VertexBuffers[0];

            var position = FindAttribute(vertexBuffer, "POSITION");

            if (position.Format == VertexFetchFormat.None)
            {
                continue;
            }

            var normal = FindAttribute(vertexBuffer, "NORMAL");
            var texCoord = FindAttribute(vertexBuffer, "TEXCOORD");

            meshletBuffers.Bind();
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, (int)ReservedBufferSlots.MeshletVertexBuffer, vertexBuffer.Handle);

            var transform = request.Node.Transform.To3x4();
            GL.ProgramUniformMatrix3x4(shader.Program, uniforms.Transform, false, ref transform);

            var instanceCount = request.Node is SceneAggregate { InstanceTransforms.Count: > 0 } aggregate
                ? aggregate.InstanceTransforms.Count
                : 1;

            GL.ProgramUniform1((uint)shader.Program, uniforms.FirstMeshlet, (uint)drawCall.FirstMeshlet);
            GL.ProgramUniform1((uint)shader.Program, uniforms.MeshletCount, (uint)drawCall.NumMeshlets);
            GL.ProgramUniform1((uint)shader.Program, uniforms.BaseInstance, request.Node.Id);
            GL.ProgramUniform1(shader.Program, uniforms.IsInstancing, instanceCount > 1 ? 1 : 0);
            GL.ProgramUniform1(shader.Program, uniforms.BaseVertex, drawCall.BaseVertex);
            GL.ProgramUniform1((uint)shader.Program, uniforms.VertexStride, vertexBuffer.ElementSizeInBytes);

            SetAttributeUniform(shader, uniforms.Position, position);
            SetAttributeUniform(shader, uniforms.Normal, normal);
            SetAttributeUniform(shader, uniforms.TexCoord, texCoord);

            // One workgroup per meshlet, the instances laid out one whole meshlet range after another
            var groupCount = (long)drawCall.NumMeshlets * instanceCount;

            counters.CountDrawCall(request.Node);
            counters.CountIndirectDraw((int)Math.Min(groupCount, int.MaxValue));

            for (var first = 0L; first < groupCount; first += GLEnvironment.MaxDrawMeshTasks)
            {
                GL.NV.DrawMeshTask((uint)first, (uint)Math.Min(GLEnvironment.MaxDrawMeshTasks, groupCount - first));
            }
        }
    }

    private static void SetAttributeUniform(Shader shader, int location, (uint Offset, VertexFetchFormat Format) attribute)
    {
        GL.ProgramUniform2((uint)shader.Program, location, attribute.Offset, (uint)attribute.Format);
    }

    /// <summary>Locates the first element of a semantic and translates its buffer format into what the fetch
    /// in the shader can read, which is <see cref="VertexFetchFormat.None"/> when the mesh has neither.</summary>
    private static (uint Offset, VertexFetchFormat Format) FindAttribute(VertexDrawBuffer vertexBuffer, string semanticName)
    {
        foreach (var field in vertexBuffer.InputLayoutFields)
        {
            if (field.SemanticIndex != 0 || !string.Equals(field.SemanticName, semanticName, StringComparison.Ordinal))
            {
                continue;
            }

            var format = field.Format switch
            {
                DXGI_FORMAT.R32G32B32_FLOAT => VertexFetchFormat.R32G32B32Float,
                DXGI_FORMAT.R32G32_FLOAT => VertexFetchFormat.R32G32Float,
                DXGI_FORMAT.R32_UINT => VertexFetchFormat.R32Uint,
                DXGI_FORMAT.R16G16_FLOAT => VertexFetchFormat.R16G16Float,
                DXGI_FORMAT.R16G16_SNORM => VertexFetchFormat.R16G16Snorm,
                DXGI_FORMAT.R16G16_UNORM => VertexFetchFormat.R16G16Unorm,
                _ => VertexFetchFormat.None,
            };

            return (field.Offset, format);
        }

        return (0u, VertexFetchFormat.None);
    }
}
