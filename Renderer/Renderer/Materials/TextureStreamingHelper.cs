using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.RHI;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>
    /// Streams texture mips in, smallest first. Thread pool work items read mips into a staging buffer, upload
    /// them on the transfer queue and wait for the copy to finish, then leave a pending transfer for the render
    /// thread, which takes ownership of the mips and lets shaders sample them.
    /// </summary>
    /// <remarks>
    /// Every texture belongs to the <see cref="RendererContext"/> that loaded it and is only drawn by that context's
    /// renderer. Sharing textures between contexts, for example a map opened in two tabs, would need reference
    /// counting, and a clock covering every renderer that draws them to know when the last one is done.
    /// </remarks>
    public sealed class TextureStreamingHelper(RendererContext rendererContext)
    {
        // Each upload blocks a thread pool thread until its copy is done, so only a few run at once
        private const int MaxConcurrentUploads = 4;

        // Smallest mips are read together, up to this size, ~128x128 with block compression
        private const long RequestByteBudget = 32 * 1024;

        // Mips that have been copied and released by the transfer queue, waiting for the graphics queue to acquire them
        private sealed record PendingTransfer(StreamedTexture Stream, int FirstMip, int LastMip);

        // A command list with its own pool and a fence, used by one upload at a time
        private sealed record UploadContext(CommandList Commands, VkFence Fence);

        // Streams waiting for an upload to pick up their next mips
        private readonly ConcurrentQueue<StreamedTexture> pendingRequests = new();

        private readonly ConcurrentQueue<PendingTransfer> pendingTransfers = new();
        private readonly ConcurrentBag<UploadContext> uploadContexts = [];

        private int activeUploads;
        private bool createdFallbackTextures;

        // Only touched by the render thread, kept to avoid allocating every frame
        private readonly List<PendingTransfer> finishing = [];
        private readonly List<VkImageMemoryBarrier2> barriers = [];

        internal void BeginStreaming(StreamedTexture stream)
        {
            pendingRequests.Enqueue(stream);
            StartUploads();
        }

        /// <summary>
        /// Uploads every mip of <paramref name="stream"/> on the calling thread, blocking until the copy is done.
        /// Shaders can sample it after the next <see cref="FinishPendingQueueTransfers"/>.
        /// </summary>
        internal void LoadNow(StreamedTexture stream)
        {
            Upload(stream, long.MaxValue);
        }

        /// <summary>
        /// Takes ownership of every mip uploaded since the last call and lets shaders sample them from this
        /// frame on. Must be called on the render thread at the start of the frame, before anything samples a texture.
        /// </summary>
        /// <param name="commands">The frame's command list, after it has begun.</param>
        /// <returns>Whether any texture got new mips, which changes its <see cref="RHI.Texture.DescriptorHandle"/>.</returns>
        internal bool FinishPendingQueueTransfers(CommandList commands)
        {
            // Done here, on the first frame, so they are resident before any material is drawn with them
            if (!createdFallbackTextures)
            {
                createdFallbackTextures = true;
                rendererContext.MaterialLoader.CreateFallbackTextures();
            }

            if (pendingTransfers.IsEmpty)
            {
                return false;
            }

            finishing.Clear();

            while (pendingTransfers.TryDequeue(out var transfer))
            {
                finishing.Add(transfer);
            }

            var device = RenderDevice!;

            // The transfer queue released the mips and has finished, since the upload waited on its fence before
            // queueing them, so the acquire needs no semaphore. Without a dedicated transfer family there was
            // no hand over, the upload already moved the mips into their final layout.
            if (device.HasDedicatedTransferFamily)
            {
                barriers.Clear();

                foreach (var transfer in finishing)
                {
                    barriers.Add(CreateMipBarrier(transfer.Stream.Texture, transfer.FirstMip, transfer.LastMip,
                        VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                        VkPipelineStageFlags2.None, VkAccessFlags2.None, VkPipelineStageFlags2.AllCommands, VkAccessFlags2.ShaderSampledRead,
                        device.QueueFamilyIndices.TransferFamily!.Value, device.QueueFamilyIndices.GraphicsFamily!.Value));
                }

                commands.BeginDebugLabel("Acquire Texture Mips");
                commands.PipelineBarrier(CollectionsMarshal.AsSpan(barriers));
                commands.EndDebugLabel();
            }

            foreach (var transfer in finishing)
            {
                var texture = transfer.Stream.Texture;

                for (var mip = transfer.FirstMip; mip <= transfer.LastMip; mip++)
                {
                    texture.MipLayouts[mip] = VkImageLayout.ShaderReadOnlyOptimal;
                }

                texture.SetResidentMip((uint)transfer.FirstMip);
            }

            return true;
        }

        // Hands waiting streams to the thread pool, as many as the upload limit allows
        private void StartUploads()
        {
            while (pendingRequests.TryDequeue(out var stream))
            {
                if (Interlocked.Increment(ref activeUploads) > MaxConcurrentUploads)
                {
                    Interlocked.Decrement(ref activeUploads);
                    pendingRequests.Enqueue(stream);

                    // An upload that finished between the increment and the enqueue may have found the queue
                    // empty, so check again rather than leaving the stream parked with nothing to pick it up
                    if (Volatile.Read(ref activeUploads) < MaxConcurrentUploads)
                    {
                        continue;
                    }

                    return;
                }

                ThreadPool.UnsafeQueueUserWorkItem(stream, preferLocal: false);
            }
        }

        /// <summary>Uploads a stream's next mips, then queues the stream again if it has more. Runs on the thread pool.</summary>
        internal void RunUpload(StreamedTexture stream)
        {
            try
            {
                if (!rendererContext.CancellationToken.IsCancellationRequested && Upload(stream, RequestByteBudget) && stream.NextMip >= 0)
                {
                    // To the back of the queue, so every texture gets its small mips before any gets its big ones
                    pendingRequests.Enqueue(stream);
                }
            }
            finally
            {
                Interlocked.Decrement(ref activeUploads);
                StartUploads();
            }
        }

        // Reads, copies and releases the stream's next mips, and waits for the copy before queueing them for the
        // render thread. Returns whether it succeeded; on failure the texture keeps the mips it already has.
        private unsafe bool Upload(StreamedTexture stream, long budget)
        {
            stream.PlanRequest(budget);

            RHI.Buffer? staging = null;
            UploadContext? context = null;

            try
            {
                staging = new RHI.Buffer((ulong)stream.RequestBytes, VkBufferUsageFlags.TransferSrc, VmaMemoryUsage.CpuOnly, name: $"{stream.Name} staging");

                var regions = new VkBufferImageCopy[stream.RequestLastMip - stream.RequestFirstMip + 1];
                var mapped = (byte*)staging.Map();

                try
                {
                    var offset = 0L;
                    var region = 0;

                    for (var mip = stream.RequestLastMip; mip >= stream.RequestFirstMip; mip--)
                    {
                        var size = stream.Mips[mip].StagingSize;

                        stream.ReadMip(mip, new Span<byte>(mapped + offset, size));
                        regions[region++] = stream.CreateCopyRegion(mip, (ulong)offset);

                        offset += StreamedTexture.AlignStagingOffset(size);
                    }
                }
                finally
                {
                    staging.Unmap();
                }

                context = RentUploadContext();
                RecordUpload(context.Commands, stream, staging, regions);

                var device = RenderDevice!;
                device.SubmitTransfer(context.Commands, context.Fence);
                device.WaitForFences(context.Fence);
                device.ResetFences(context.Fence);

                pendingTransfers.Enqueue(new PendingTransfer(stream, stream.RequestFirstMip, stream.RequestLastMip));
                stream.NextMip = stream.RequestFirstMip - 1;

                return true;
            }
            catch (Exception e)
            {
                rendererContext.Logger.LogError(e, "Failed to stream texture {Name}", stream.Name);
                return false;
            }
            finally
            {
                // The copy has finished or was never submitted, so nothing on the GPU reads it any more
                staging?.Destroy();

                if (context != null)
                {
                    uploadContexts.Add(context);
                }
            }
        }

        private UploadContext RentUploadContext()
        {
            if (uploadContexts.TryTake(out var context))
            {
                return context;
            }

            var device = RenderDevice!;
            return new UploadContext(new CommandList(device.QueueFamilyIndices.TransferFamily!.Value), device.CreateFence());
        }

        private static unsafe void RecordUpload(CommandList commands, StreamedTexture stream, RHI.Buffer staging, VkBufferImageCopy[] regions)
        {
            var device = RenderDevice!;
            var texture = stream.Texture;
            var firstMip = stream.RequestFirstMip;
            var lastMip = stream.RequestLastMip;

            commands.Begin();

            var toTransferDst = CreateMipBarrier(texture, firstMip, lastMip, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
                VkPipelineStageFlags2.None, VkAccessFlags2.None, VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite);

            commands.PipelineBarrier([toTransferDst]);

            fixed (VkBufferImageCopy* pRegions = regions)
            {
                device.VkDeviceApi.vkCmdCopyBufferToImage(commands.Handle, staging.Handle, texture.ImageHandle,
                    VkImageLayout.TransferDstOptimal, (uint)regions.Length, pRegions);
            }

            // With a dedicated transfer family this releases the mips to the graphics family, which acquires them
            // in FinishPendingQueueTransfers. Both halves name the same layouts, the transition happens once.
            var release = device.HasDedicatedTransferFamily
                ? CreateMipBarrier(texture, firstMip, lastMip, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                    VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite, VkPipelineStageFlags2.None, VkAccessFlags2.None,
                    device.QueueFamilyIndices.TransferFamily!.Value, device.QueueFamilyIndices.GraphicsFamily!.Value)
                : CreateMipBarrier(texture, firstMip, lastMip, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                    VkPipelineStageFlags2.Transfer, VkAccessFlags2.TransferWrite, VkPipelineStageFlags2.AllCommands, VkAccessFlags2.ShaderSampledRead);

            commands.PipelineBarrier([release]);
            commands.End();
        }

        private static VkImageMemoryBarrier2 CreateMipBarrier(RHI.Texture texture, int firstMip, int lastMip, VkImageLayout oldLayout, VkImageLayout newLayout,
            VkPipelineStageFlags2 srcStage, VkAccessFlags2 srcAccess, VkPipelineStageFlags2 dstStage, VkAccessFlags2 dstAccess,
            uint srcQueueFamily = VK_QUEUE_FAMILY_IGNORED, uint dstQueueFamily = VK_QUEUE_FAMILY_IGNORED)
        {
            return new VkImageMemoryBarrier2
            {
                srcStageMask = srcStage,
                srcAccessMask = srcAccess,
                dstStageMask = dstStage,
                dstAccessMask = dstAccess,
                oldLayout = oldLayout,
                newLayout = newLayout,
                srcQueueFamilyIndex = srcQueueFamily,
                dstQueueFamilyIndex = dstQueueFamily,
                image = texture.ImageHandle,
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VkImageAspectFlags.Color,
                    baseMipLevel = (uint)firstMip,
                    levelCount = (uint)(lastMip - firstMip + 1),
                    baseArrayLayer = 0,
                    layerCount = texture.ArrayLayers,
                },
            };
        }

        /// <summary>
        /// Throws away every stream waiting for an upload. Uploads still running are not waited for,
        /// see <see cref="DrainPendingLoads"/>.
        /// </summary>
        public void CancelAllStreaming()
        {
            pendingRequests.Clear();
        }

        /// <summary>
        /// Waits for uploads running on the thread pool to finish, so nothing is left reading a texture resource
        /// that the caller is about to dispose. Never call this on the UI thread.
        /// </summary>
        public void DrainPendingLoads()
        {
            CancelAllStreaming();

            var backoff = new SpinWait();
            var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency;

            while (Volatile.Read(ref activeUploads) > 0)
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    rendererContext.Logger.LogError("Gave up waiting on {Count} texture uploads", Volatile.Read(ref activeUploads));
                    break;
                }

                backoff.SpinOnce();
            }
        }
    }
}
