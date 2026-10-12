using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Vortice.Vulkan;

namespace ValveResourceFormat.Renderer.RHI;

public struct RenderingAttachmentInfo
{
    required public Image image;
    public VkAttachmentLoadOp loadOp = VkAttachmentLoadOp.Load;
    public VkAttachmentStoreOp storeOp = VkAttachmentStoreOp.Store;
    public Image? resolveImage = null;
    public VkResolveModeFlags resolveMode;
    public VkClearValue clearValue = new VkClearValue { color = new VkClearColorValue(0.0f, 0.0f, 0.0f, 1.0f) };

    public RenderingAttachmentInfo()
    { }
}

internal struct RenderingInfo
{
    public VkRect2D renderArea;
    public uint layerCount = 1;
    required public RenderingAttachmentInfo[] colorAttachments;
    public RenderingAttachmentInfo? depthAttachment;

    public RenderingInfo() { }

    [SetsRequiredMembers]
    public RenderingInfo(RenderTarget target)
    {
        //offset is implied to be zero.
        renderArea.extent = new VkExtent2D(target.ColorTarget.Width, target.ColorTarget.Height);
        colorAttachments = [new RenderingAttachmentInfo { image = target.ColorTarget }];
        depthAttachment = new RenderingAttachmentInfo { image = target.DepthTarget };
    }
}

unsafe public class CommandList : IDisposable
{
    private readonly VkCommandPool _commandPool;

    public VkCommandBuffer Handle { get; }

    public uint QueueFamilyIndex { get; private set; }

    /// <summary>Size of the push constant range of the shared pipeline layout, in bytes.</summary>
    public const uint PushConstantSize = 24;

    /// <summary>Largest update <see cref="UpdateBuffer(Buffer, ReadOnlySpan{byte}, ulong)"/> accepts, in bytes.</summary>
    public const int MaxUpdateBufferSize = 65536;

    private bool insideRendering;

    public CommandList(uint queueFamilyIndex)
    {
        var device = RenderDevice
            ?? throw new InvalidOperationException(
                "S2vDevice has not been initialized.");

        var poolInfo = new VkCommandPoolCreateInfo
        {
            flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
            queueFamilyIndex = queueFamilyIndex
        };

        QueueFamilyIndex = queueFamilyIndex;

        var result = device.VkDeviceApi.vkCreateCommandPool(
            &poolInfo,
            null,
            out _commandPool);

        if (result != VkResult.Success)
            throw new Exception(
                $"Failed to create command pool: {result}");

        var allocInfo = new VkCommandBufferAllocateInfo
        {
            commandPool = _commandPool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1
        };

        VkCommandBuffer cmdBuff;

        result = device.VkDeviceApi.vkAllocateCommandBuffers(
            &allocInfo,
            &cmdBuff);

        if (result != VkResult.Success)
            throw new Exception(
                $"Failed to allocate command buffer: {result}");

        Handle = cmdBuff;
    }

    public void Begin()
    {
        var device = RenderDevice!;

        device.VkDeviceApi.vkResetCommandBuffer(
            Handle,
            0);

        var beginInfo = new VkCommandBufferBeginInfo
        {
            flags = VkCommandBufferUsageFlags.OneTimeSubmit
        };

        Check(
            device.VkDeviceApi.vkBeginCommandBuffer(
                Handle,
                &beginInfo),
            "vkBeginCommandBuffer");
        if (device.QueueFamilyIndices.GraphicsFamily == QueueFamilyIndex)
            fixed (VkDescriptorSet* pSharedBindlessSet = &RenderDevice.SharedBindlessDescriptorSet)
            {
                RenderDevice!.VkDeviceApi.vkCmdBindDescriptorSets(
                    Handle,
                    pipelineBindPoint: VkPipelineBindPoint.Graphics,
                    layout: RenderDevice.SharedPipelineLayout,
                    firstSet: 1,
                    descriptorSetCount: 1,
                    pSharedBindlessSet,
                    dynamicOffsetCount: 0,
                    dynamicOffsets: null);
                RenderDevice!.VkDeviceApi.vkCmdBindDescriptorSets(
                    Handle,
                    pipelineBindPoint: VkPipelineBindPoint.Compute,
                    layout: RenderDevice.SharedPipelineLayout,
                    firstSet: 1,
                    descriptorSetCount: 1,
                    pSharedBindlessSet,
                    dynamicOffsetCount: 0,
                    dynamicOffsets: null);
            }
    }

    internal void BlitImageToImage(Image a, Image b)
    {
        Debug.Assert(a.ArrayLayers == b.ArrayLayers, "Blitting requires matching layer counts");

        VkImageBlit2 blit = new()
        {
            srcSubresource = new VkImageSubresourceLayers
            {
                aspectMask = VkImageAspectFlags.Color,
                mipLevel = 0,
                baseArrayLayer = 0,
                layerCount = a.ArrayLayers
            },
            dstSubresource = new VkImageSubresourceLayers
            {
                aspectMask = VkImageAspectFlags.Color,
                mipLevel = 0,
                baseArrayLayer = 0,
                layerCount = b.ArrayLayers
            }
        };

        blit.srcOffsets[0] = new VkOffset3D(0, 0, 0);
        blit.srcOffsets[1] = new VkOffset3D((int)a.Width, (int)a.Height, (int)a.Depth);

        blit.dstOffsets[0] = new VkOffset3D(0, 0, 0);
        blit.dstOffsets[1] = new VkOffset3D((int)b.Width, (int)b.Height, (int)b.Depth);

        VkBlitImageInfo2 info = new()
        {
            srcImage = a.ImageHandle,
            srcImageLayout = a.MipLayouts[0],
            dstImage = b.ImageHandle,
            dstImageLayout = b.MipLayouts[0],
            pRegions = &blit,
            regionCount = 1
        };

        RenderDevice!.VkDeviceApi.vkCmdBlitImage2(Handle, &info);
    }

    internal void ClearColorImage(Image image, VkClearColorValue color)
    {
        var subresourceRange = new VkImageSubresourceRange
        {
            aspectMask = VkImageAspectFlags.Color,
            baseMipLevel = 0,
            levelCount = image.MipLevels,
            baseArrayLayer = 0,
            layerCount = image.ArrayLayers
        };

        RenderDevice!.VkDeviceApi.vkCmdClearColorImage(
            Handle,
            image.ImageHandle,
            image.MipLayouts[0],
            &color,
            1,
            &subresourceRange
            );
    }

    internal void ClearDepthStencilImage(Image image, float depthValue, uint stencilValue)
    {
        //TODO: Stencil aspect?
        var subresourceRange = new VkImageSubresourceRange
        {
            aspectMask = VkImageAspectFlags.Depth,
            baseMipLevel = 0,
            levelCount = image.MipLevels,
            baseArrayLayer = 0,
            layerCount = image.ArrayLayers
        };

        VkClearDepthStencilValue value = new VkClearDepthStencilValue(depthValue, stencilValue);

        RenderDevice!.VkDeviceApi.vkCmdClearDepthStencilImage(
            Handle,
            image.ImageHandle,
            image.MipLayouts[0],
            &value,
            1,
            &subresourceRange
            );
    }

    internal void ClearRenderTarget(RenderTarget target, VkClearColorValue color, float depthValue, uint stencilValue = 0)
    {
        ImageTransitionBarrier(
            target.ColorTarget,
            VkImageLayout.TransferDstOptimal,
            srcBeforeTransition: VkPipelineStageFlags2.AllCommands | VkPipelineStageFlags2.ColorAttachmentOutput,
            dstTransitionBefore: VkPipelineStageFlags2.AllCommands,
            srcMask: VkAccessFlags2.ColorAttachmentWrite | VkAccessFlags2.ShaderWrite | VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ShaderRead,
            dstMask: VkAccessFlags2.TransferWrite
            );

        ImageTransitionBarrier(
            target.DepthTarget,
            VkImageLayout.TransferDstOptimal,
            srcBeforeTransition: VkPipelineStageFlags2.AllCommands | VkPipelineStageFlags2.ColorAttachmentOutput,
            dstTransitionBefore: VkPipelineStageFlags2.AllCommands,
            srcMask: VkAccessFlags2.ColorAttachmentWrite | VkAccessFlags2.ShaderWrite | VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ShaderRead,
            dstMask: VkAccessFlags2.TransferWrite,
            aspectFlags: VkImageAspectFlags.Depth
            );
        ClearColorImage(target.ColorTarget, color);
        ClearDepthStencilImage(target.DepthTarget, depthValue, stencilValue);

        ImageTransitionBarrier(
            target.ColorTarget,
            VkImageLayout.ColorAttachmentOptimal,
            srcBeforeTransition: VkPipelineStageFlags2.AllCommands | VkPipelineStageFlags2.ColorAttachmentOutput,
            dstTransitionBefore: VkPipelineStageFlags2.AllCommands,
            srcMask: VkAccessFlags2.ColorAttachmentWrite | VkAccessFlags2.ShaderWrite | VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ShaderRead,
            dstMask: VkAccessFlags2.TransferWrite
            );

        ImageTransitionBarrier(
            target.DepthTarget,
            VkImageLayout.DepthAttachmentOptimal,
            srcBeforeTransition: VkPipelineStageFlags2.AllCommands | VkPipelineStageFlags2.ColorAttachmentOutput,
            dstTransitionBefore: VkPipelineStageFlags2.AllCommands,
            srcMask: VkAccessFlags2.ColorAttachmentWrite | VkAccessFlags2.ShaderWrite | VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ShaderRead,
            dstMask: VkAccessFlags2.TransferWrite,
            aspectFlags: VkImageAspectFlags.Depth
            );
    }
    public void ClearSwapchainImage(
        Image image,
        VkClearColorValue color)
    {
        var device = RenderDevice!;
        ImageTransitionBarrier(
            image,
            VkImageLayout.TransferDstOptimal,
            srcBeforeTransition: VkPipelineStageFlags2.AllCommands | VkPipelineStageFlags2.ColorAttachmentOutput,
            dstTransitionBefore: VkPipelineStageFlags2.AllCommands,
            srcMask: VkAccessFlags2.ColorAttachmentWrite | VkAccessFlags2.ShaderWrite | VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ShaderRead,
            dstMask: VkAccessFlags2.TransferWrite
            );

        ClearColorImage(image, color);

        ImageTransitionBarrier(
            image,
            VkImageLayout.ColorAttachmentOptimal,
            srcBeforeTransition: VkPipelineStageFlags2.AllCommands,
            dstTransitionBefore: VkPipelineStageFlags2.AllCommands,
            srcMask: VkAccessFlags2.TransferWrite,
            dstMask: VkAccessFlags2.ColorAttachmentWrite | VkAccessFlags2.ShaderWrite | VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ShaderRead
            );
    }

    public void End()
    {
        var device = RenderDevice!;

        Check(
            device.VkDeviceApi.vkEndCommandBuffer(Handle),
            "vkEndCommandBuffer");
    }

    public void ImageTransitionBarrier(
        Image image,
        VkImageLayout targetLayout,
        uint mipLevel = 0,
        VkPipelineStageFlags2 srcBeforeTransition = 0,
        VkPipelineStageFlags2 dstTransitionBefore = 0,
        VkAccessFlags2 srcMask = 0,
        VkAccessFlags2 dstMask = 0,

        VkImageAspectFlags aspectFlags = VkImageAspectFlags.Color
        )
    {
        var device = RenderDevice!;

        var barrier = new VkImageMemoryBarrier2
        {
            oldLayout = image.MipLayouts[mipLevel],
            newLayout = targetLayout,
            srcAccessMask = srcMask,
            srcStageMask = srcBeforeTransition,
            dstAccessMask = dstMask,
            dstStageMask = dstTransitionBefore,
            image = image.ImageHandle,
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = aspectFlags,
                baseMipLevel = mipLevel,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = image.ArrayLayers
            }
        };

        PipelineBarrier([barrier]);
        image.MipLayouts[mipLevel] = targetLayout;
    }

    public void PipelineBarrier(ReadOnlySpan<VkImageMemoryBarrier2> imageMemoryBarriers = new ReadOnlySpan<VkImageMemoryBarrier2>(), ReadOnlySpan<VkBufferMemoryBarrier2> bufferMemoryBarriers = new ReadOnlySpan<VkBufferMemoryBarrier2>(), ReadOnlySpan<VkMemoryBarrier2> memoryBarriers = new ReadOnlySpan<VkMemoryBarrier2>())
    {
        fixed (VkImageMemoryBarrier2* pImageMemoryBarriers = imageMemoryBarriers)
        fixed (VkBufferMemoryBarrier2* pBufferMemoryBarriers = bufferMemoryBarriers)
        fixed (VkMemoryBarrier2* pMemoryBarriers = memoryBarriers)
        {
            VkDependencyInfo depInfo = new VkDependencyInfo
            {
                memoryBarrierCount = (uint)memoryBarriers.Length,
                pMemoryBarriers = pMemoryBarriers,
                bufferMemoryBarrierCount = (uint)bufferMemoryBarriers.Length,
                pBufferMemoryBarriers = pBufferMemoryBarriers,
                imageMemoryBarrierCount = (uint)imageMemoryBarriers.Length,
                pImageMemoryBarriers = pImageMemoryBarriers
            };

            RenderDevice!.VkDeviceApi.vkCmdPipelineBarrier2(Handle, &depInfo);
        }
        //H7per: TODO: We should warn if nothing was submitted.
    }

    /// <summary>Records a global memory barrier, covering every resource.</summary>
    public void MemoryBarrier(
        VkPipelineStageFlags2 srcStage,
        VkAccessFlags2 srcAccess,
        VkPipelineStageFlags2 dstStage,
        VkAccessFlags2 dstAccess)
    {
        var barrier = new VkMemoryBarrier2
        {
            srcStageMask = srcStage,
            srcAccessMask = srcAccess,
            dstStageMask = dstStage,
            dstAccessMask = dstAccess,
        };

        PipelineBarrier(memoryBarriers: new ReadOnlySpan<VkMemoryBarrier2>(in barrier));
    }

    /// <summary>Records one barrier per buffer, each covering the whole buffer.</summary>
    public void BufferBarrier(
        VkPipelineStageFlags2 srcStage,
        VkAccessFlags2 srcAccess,
        VkPipelineStageFlags2 dstStage,
        VkAccessFlags2 dstAccess,
        params ReadOnlySpan<Buffer> buffers)
    {
        Span<VkBufferMemoryBarrier2> barriers = stackalloc VkBufferMemoryBarrier2[buffers.Length];

        for (var i = 0; i < buffers.Length; i++)
        {
            barriers[i] = new VkBufferMemoryBarrier2
            {
                srcStageMask = srcStage,
                srcAccessMask = srcAccess,
                dstStageMask = dstStage,
                dstAccessMask = dstAccess,
                srcQueueFamilyIndex = Vulkan.VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = Vulkan.VK_QUEUE_FAMILY_IGNORED,
                buffer = buffers[i].Handle,
                offset = 0,
                size = Vulkan.VK_WHOLE_SIZE,
            };
        }

        PipelineBarrier(bufferMemoryBarriers: barriers);
    }

    /// <summary>
    /// Writes <paramref name="data"/> into <paramref name="buffer"/> in command order. The data is copied into
    /// the command buffer when recorded, so the source may be reused right away. Synchronize with
    /// <see cref="BufferBarrier"/> as a transfer write.
    /// </summary>
    /// <remarks>The buffer needs <see cref="VkBufferUsageFlags.TransferDst"/> usage.</remarks>
    public void UpdateBuffer(Buffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        if (data.IsEmpty || data.Length > MaxUpdateBufferSize || data.Length % 4 != 0 || offset % 4 != 0)
        {
            throw new ArgumentException($"vkCmdUpdateBuffer takes 4 byte aligned updates of 4 to {MaxUpdateBufferSize} bytes, got {data.Length} at offset {offset}");
        }

        Debug.Assert(offset + (ulong)data.Length <= buffer.Size, "Update runs past the end of the buffer");
        Debug.Assert(!insideRendering, "vkCmdUpdateBuffer cannot be recorded inside a render pass instance");

        fixed (byte* pData = data)
        {
            RenderDevice!.VkDeviceApi.vkCmdUpdateBuffer(Handle, buffer.Handle, offset, (ulong)data.Length, pData);
        }
    }

    /// <inheritdoc cref="UpdateBuffer(Buffer, ReadOnlySpan{byte}, ulong)"/>
    public void UpdateBuffer<T>(Buffer buffer, in T data, ulong offset = 0) where T : unmanaged
    {
        UpdateBuffer(buffer, MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in data)), offset);
    }

    public void BindGraphicsPipeline(PipelineGraphics pipeline)
    {
        RenderDevice!.VkDeviceApi.vkCmdBindPipeline(Handle, VkPipelineBindPoint.Graphics, pipeline.Handle);
    }

    public void BindVertexBuffer(Buffer vertexBuffer, uint binding = 0)
    {
        RenderDevice!.VkDeviceApi.vkCmdBindVertexBuffer(Handle, binding, vertexBuffer.Handle);
    }

    public void BindIndexBuffer(Buffer indexBuffer, VkIndexType indexType = VkIndexType.Uint32)
    {
        RenderDevice!.VkDeviceApi.vkCmdBindIndexBuffer(Handle, indexBuffer.Handle, 0, indexType);
    }

    //Might need an overload to set multiple.
    public void SetViewport(VkViewport viewport)
    {
        RenderDevice!.VkDeviceApi.vkCmdSetViewport(Handle, 0, viewport);
    }

    public void SetScissor(VkRect2D scissor)
    {
        RenderDevice!.VkDeviceApi.vkCmdSetScissor(Handle, 0, scissor);
    }

    public void PushConstants<T>(T data, uint offset = 0) where T : struct
    {
        if (offset + sizeof(T) > PushConstantSize)
        {
            throw new ArgumentException($"The size of {nameof(T)} exceeds the available push constant range");
        }
        RenderDevice!.VkDeviceApi.vkCmdPushConstants(Handle, RenderDevice!.SharedPipelineLayout, VkShaderStageFlags.All, offset, (uint)sizeof(T), &data);
    }

    public void BeginDebugLabel(string name, (float r, float g, float b, float a)? labelColor = null)
    {
        (float r, float g, float b, float a) HashToColor(string name)
        {
            uint hash = 2166136261; // FNV-1a offset basis
            foreach (byte by in Encoding.UTF8.GetBytes(name))
            {
                hash ^= by;
                hash *= 16777619; // FNV prime
            }

            float r = ((hash >> 16) & 0xFF) / 255f;
            float g = ((hash >> 8) & 0xFF) / 255f;
            float b = (hash & 0xFF) / 255f;
            return (1 - (1 - r) * 0.8f, 1 - (1 - g) * 0.8f, 1 - (1 - b) * 0.8f, 1f);
        }

        var c = labelColor ?? HashToColor(name);

        //Q: Performance?
        int byteCount = Encoding.UTF8.GetByteCount(name);
        Span<byte> nameBytes = byteCount < 256 ? stackalloc byte[byteCount + 1] : new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(name, nameBytes);
        nameBytes[byteCount] = 0;

        fixed (byte* pName = nameBytes)
        fixed (float* pColor = new float[4] { c.r, c.g, c.b, c.a })
        {
            VkDebugUtilsLabelEXT labelInfo = new()
            {
                pLabelName = pName,
            };

            labelInfo.color[0] = c.r;
            labelInfo.color[1] = c.g;
            labelInfo.color[2] = c.b;
            labelInfo.color[3] = c.a;

            RenderDevice!.VkInstanceApi.vkCmdBeginDebugUtilsLabelEXT(Handle, &labelInfo);
        }
    }

    public void EndDebugLabel()
    {
        RenderDevice!.VkInstanceApi.vkCmdEndDebugUtilsLabelEXT(Handle);
    }

    internal void BeginRendering(RenderingInfo renderingInfo)
    {
        VkRenderingInfo VkRenderingInfo = new();

        if (renderingInfo.renderArea.extent.width == 0 || renderingInfo.renderArea.extent.height == 0)
        {
            VkRenderingInfo.renderArea = new VkRect2D
            {
                offset = new(),
                extent = new VkExtent2D { width = renderingInfo.colorAttachments[0].image!.Width, height = renderingInfo.colorAttachments[0].image!.Height }
            };
        }
        else
        {
            VkRenderingInfo.renderArea = renderingInfo.renderArea;
        }

        VkRenderingInfo.layerCount = renderingInfo.layerCount;
        VkRenderingInfo.colorAttachmentCount = (uint)renderingInfo.colorAttachments.Length;

        VkRenderingAttachmentInfo[] colorAttachmentArray = new VkRenderingAttachmentInfo[renderingInfo.colorAttachments.Length];

        for (uint i = 0; i < colorAttachmentArray.Length; i++)
        {
            colorAttachmentArray[i].sType = VkStructureType.RenderingAttachmentInfo;
            colorAttachmentArray[i].imageView = renderingInfo.colorAttachments[i].image.ImageViewHandle;
            colorAttachmentArray[i].imageLayout = renderingInfo.colorAttachments[i].image.MipLayouts[0];
            colorAttachmentArray[i].loadOp = renderingInfo.colorAttachments[i].loadOp;
            colorAttachmentArray[i].storeOp = renderingInfo.colorAttachments[i].storeOp;
            colorAttachmentArray[i].clearValue = renderingInfo.colorAttachments[i].clearValue;

            if (renderingInfo.colorAttachments[i].resolveImage != null)
            {
                colorAttachmentArray[i].resolveMode = renderingInfo.colorAttachments[i].resolveMode;
                colorAttachmentArray[i].resolveImageView = renderingInfo.colorAttachments[i].resolveImage!.ImageViewHandle;
                colorAttachmentArray[i].resolveImageLayout = renderingInfo.colorAttachments[i].resolveImage!.MipLayouts[0];
            }
        }

        VkRenderingAttachmentInfo* pDepthAttachment = null;

        VkRenderingAttachmentInfo depthAttachment;
        if (renderingInfo.depthAttachment.HasValue)
        {
            depthAttachment = new()
            {
                imageView = renderingInfo.depthAttachment.Value.image.ImageViewHandle,
                imageLayout = renderingInfo.depthAttachment.Value.image.MipLayouts[0],
                loadOp = renderingInfo.depthAttachment.Value.loadOp,
                storeOp = renderingInfo.depthAttachment.Value.storeOp,
                clearValue = renderingInfo.depthAttachment.Value.clearValue,

            };

            if (renderingInfo.depthAttachment.Value.resolveImage != null)
            {
                depthAttachment.resolveMode = renderingInfo.depthAttachment.Value.resolveMode;
                depthAttachment.resolveImageView = renderingInfo.depthAttachment.Value.resolveImage!.ImageViewHandle;
                depthAttachment.resolveImageLayout = renderingInfo.depthAttachment.Value.resolveImage!.MipLayouts[0];
            }

            pDepthAttachment = &depthAttachment;
        }

        VkRenderingInfo.pDepthAttachment = pDepthAttachment;

        fixed (VkRenderingAttachmentInfo* pAttachmentInfos = colorAttachmentArray)
        {
            VkRenderingInfo.pColorAttachments = pAttachmentInfos;
            BeginRendering(VkRenderingInfo);
        }
    }

    public void BeginRendering(VkRenderingInfo renderingInfo)
    {
        RenderDevice!.VkDeviceApi.vkCmdBeginRendering(Handle, &renderingInfo);
        insideRendering = true;
    }

    public void EndRendering()
    {
        RenderDevice!.VkDeviceApi.vkCmdEndRendering(Handle);
        insideRendering = false;
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        RenderDevice!.VkDeviceApi.vkCmdDraw(Handle, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        RenderDevice!.VkDeviceApi.vkCmdDrawIndexed(Handle, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    private static void Check(
        VkResult result,
        string operation)
    {
        if (result != VkResult.Success)
            throw new Exception(
                $"{operation} failed: {result}");
    }

    public void Dispose()
    {
        var device = RenderDevice;

        if (device == null)
            return;

        device.VkDeviceApi.vkDestroyCommandPool(
            _commandPool,
            null);
    }
}
