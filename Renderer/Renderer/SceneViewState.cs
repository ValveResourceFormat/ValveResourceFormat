using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.RHI;
using ValveResourceFormat.Renderer.SceneNodes;
using Vortice.Vulkan;

namespace ValveResourceFormat.Renderer;

/// <summary>Culling results and draw lists for drawing a scene through one view.</summary>
public sealed class SceneViewState
{
    /// <summary>Gets the scene this state draws.</summary>
    public Scene Scene { get; }

    /// <summary>
    /// Gets the view constants this state draws with. Each state has its own, so every view can be uploaded
    /// before the render pass instead of between draws.
    /// </summary>
    internal RHI.Buffer ViewBuffer { get; }

    // VKTODO: Bring back the light binner, culling results, render lists, indirect draw buffers and the depth pyramid

    /// <summary>Initializes a view of an already initialized <paramref name="scene"/>.</summary>
    public SceneViewState(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
        ViewBuffer = new RHI.Buffer((ulong)Marshal.SizeOf<ViewConstants>(), VkBufferUsageFlags.UniformBuffer | VkBufferUsageFlags.TransferDst,
            VmaMemoryUsage.AutoPreferDevice, name: nameof(ViewConstants));
    }

    /// <summary>
    /// Renders the opaque pass, optionally with a depth prepass, followed by aggregate indirect draws and static overlay geometry.
    /// </summary>
    /// <remarks>
    /// VKTODO: Only the plain opaque draws are ported. They come from every dynamic model node instead of the culled
    /// render lists, and there is no depth prepass, no aggregate indirect draws and no static overlay geometry yet.
    /// </remarks>
    /// <param name="cmd">The command list, inside a render pass instance with this view bound.</param>
    internal void RenderOpaqueLayer(CommandList cmd)
    {
        foreach (var node in Scene.DynamicNodes)
        {
            if (node is not ModelSceneNode modelNode)
            {
                continue;
            }

            foreach (var renderableMesh in modelNode.RenderableMeshes)
            {
                foreach (var drawCall in renderableMesh.DrawCallsOpaque)
                {
                    // Read once, so the bound pipeline and the layout the parameters are filled for cannot differ
                    var pipeline = drawCall.Material.Pipeline.Current;

                    cmd.BindGraphicsPipeline(pipeline);
                    cmd.PushConstants(drawCall.Material.PrepareParameterBuffer(pipeline), RenderMaterial.ParameterBufferPushConstantOffset);

                    cmd.BindVertexBuffer(drawCall.VertexBuffers[0]);
                    cmd.BindIndexBuffer(drawCall.IndexBuffer, VkIndexType.Uint16);

                    cmd.DrawIndexed((uint)drawCall.IndexCount, 1, (uint)drawCall.StartIndex / 2, drawCall.VertexOffset, 0);
                }
            }
        }
    }
}
