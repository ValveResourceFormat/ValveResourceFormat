using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>Loads texture mip data in the background and hooks it up to the texture over the frames
    /// after load, so scene load time stops scaling with texture volume. Named after Source 2's own
    /// <c>CTextureStreamingHelper</c>, and borrowing its vocabulary: mip data is "bits", a chain's next
    /// read is a "load request", and the per-frame work is time sliced.</summary>
    public sealed class TextureStreamingHelper(RendererContext rendererContext)
    {
        /// <summary>Streams waiting to issue their next load request: newly loaded textures and chains parked by the throttle.</summary>
        private readonly ConcurrentQueue<StreamedTexture> pendingLoadRequests = new();

        /// <summary>Loaded mip bits waiting to be hooked up. A single FIFO queue: load requests are
        /// issued sequentially per texture, so it preserves each texture's chain order by construction.</summary>
        private readonly ConcurrentQueue<LoadedMipBits> loadedMipBits = new();

        /// <summary>Throttle on mip buffer bytes in flight between a load request and its hookup. Chains
        /// park at the cap and the next time slice resumes them, which bounds the working set the shared
        /// array pool must cover and keeps the managed heap bounded before the render loop is running.</summary>
        private const long MaxBitsInFlight = 256L * 1024 * 1024;

        /// <summary>Buffer bytes between a load request and its hookup. A soft gate, adjusted with interlocked adds by the hookup and by failed loads.</summary>
        private long bitsInFlight;

        /// <summary>When set, one load request reads its texture's whole remaining chain into one buffer
        /// and hooks it up with a single storage recreation — one texture per worker, batched like the
        /// synchronous path but with textures in parallel. For one-shot consumers that drain to completion,
        /// like the thumbnail renderer; the scene render loop stays per-mip, whose small work items its
        /// time slice needs.</summary>
        public bool LoadWholeChains { get; set; }

        /// <summary>Incomplete streams by their texture, so a non-streaming request for a texture that a
        /// material already started streaming can finish the chain inline instead of sampling a stub.</summary>
        private readonly ConcurrentDictionary<RenderTexture, StreamedTexture> incompleteStreams = new();

        /// <summary>Takes over a freshly loaded texture that holds only its smallest mip, queueing the
        /// load request for the rest of its chain.</summary>
        internal void BeginStreaming(StreamedTexture stream)
        {
            incompleteStreams.TryAdd(stream.Texture, stream);
            pendingLoadRequests.Enqueue(stream);
        }

        /// <summary>Retires a chain at any of its terminal points: completed, failed, or dropped with its
        /// texture. Idempotent — a retired chain can still be dequeued from the pending queue by a later drain.</summary>
        private void RetireStream(StreamedTexture stream)
        {
            incompleteStreams.TryRemove(stream.Texture, out _);
        }

        /// <summary>Drops all pending stream work, the counterpart to <see cref="FinishAllStreaming"/>.
        /// Makes no GL calls, so it is also safe during teardown without a current context. The throttle
        /// is unwound per item rather than zeroed: a straggling load's bytes stay counted until its bits
        /// surface, so the counter never goes negative.</summary>
        public void CancelAllStreaming()
        {
            while (pendingLoadRequests.TryDequeue(out var stream))
            {
                RetireStream(stream);
            }

            // Drain rather than clear: these buffers belong to the pool, not the garbage collector
            while (loadedMipBits.TryDequeue(out var bits))
            {
                ArrayPool<byte>.Shared.Return(bits.Buffer);
                Interlocked.Add(ref bitsInFlight, -bits.ByteSize);
                RetireStream(bits.Stream);
            }

            incompleteStreams.Clear();
        }

        /// <summary>Hooks up one loaded mip level: recreates the storage one level larger, copying the
        /// resident smaller levels over, then sub-images the new mip into the new top level. One texture's
        /// bits arrive in chain order, smallest mip first, so the storage only ever holds defined levels
        /// — nothing can sample as black — and VRAM stays tightly packed to what has arrived.</summary>
        private static void HookUpMipBits(in LoadedMipBits bits)
        {
            var stream = bits.Stream;
            var texture = stream.Texture;

            if (texture.Handle == 0)
            {
                return; // deleted while the load was in flight; drop the data
            }

            Debug.Assert(bits.MipCount == 1 || stream.Mips[stream.NextMip] == bits.Mip);

            // Grow once, to the largest mip in the batch; level indices then count from the new top
            var lastMip = bits.MipCount == 1 ? bits.Mip : stream.Mips[stream.NextMip + bits.MipCount - 1];
            AddMipLevels(stream, lastMip.ChainLevel);

            var offset = 0;

            for (var i = 0; i < bits.MipCount; i++)
            {
                var mip = i == 0 ? bits.Mip : stream.Mips[stream.NextMip + i];

                SubImageMip(stream, mip.ChainLevel - stream.ResidentChainLevel, mip, bits.Buffer, offset);
                offset += stream.InPlaceSize(mip);
            }
        }

        /// <summary>Uploads one mip's texel data from an offset within a buffer to a storage level.</summary>
        private static unsafe void SubImageMip(StreamedTexture stream, int level, in PlannedMip mip, byte[] buffer, int offset)
        {
            var texture = stream.Texture;

            fixed (byte* data = &buffer[offset])
            {
                if (stream.Format.IsBlockCompressed())
                {
                    if (stream.Is3D)
                    {
                        GL.CompressedTextureSubImage3D(texture.Handle, level, 0, 0, 0, mip.Width, mip.Height, mip.Depth, (PixelFormat)stream.SizedInternalFormat, mip.BufferSize, (IntPtr)data);
                    }
                    else
                    {
                        GL.CompressedTextureSubImage2D(texture.Handle, level, 0, 0, mip.Width, mip.Height, (PixelFormat)stream.SizedInternalFormat, mip.BufferSize, (IntPtr)data);
                    }
                }
                else
                {
                    if (stream.Is3D)
                    {
                        GL.TextureSubImage3D(texture.Handle, level, 0, 0, 0, mip.Width, mip.Height, mip.Depth, stream.Format.ToGLPixelFormat(), stream.Format.ToGLPixelType(), (IntPtr)data);
                    }
                    else
                    {
                        GL.TextureSubImage2D(texture.Handle, level, 0, 0, mip.Width, mip.Height, stream.Format.ToGLPixelFormat(), stream.Format.ToGLPixelType(), (IntPtr)data);
                    }
                }
            }
        }

        /// <summary>Recreates a streamed texture's storage sized for the given chain level, copies the
        /// resident smaller levels into its tail and swaps the texture over to the new object. No-op for
        /// levels the current storage already covers, such as the synchronous smallest mip at load.</summary>
        private static void AddMipLevels(StreamedTexture stream, int chainLevel)
        {
            if (chainLevel >= stream.ResidentChainLevel)
            {
                return;
            }

            var texture = stream.Texture;
            var target = texture.Target;
            var levels = stream.ChainLevels - chainLevel;
            var (width, height, depth) = MaterialLoader.GetChainLevelSize(target, stream.AllocWidth, stream.AllocHeight, stream.AllocDepth, chainLevel);

            var newHandle = GraphicsDevice.CreateTexture(target, stream.Name);
            MaterialLoader.CreateStorageForTarget(newHandle, target, levels, stream.SizedInternalFormat, width, height, depth);

            for (var c = stream.ResidentChainLevel; c < stream.ChainLevels; c++)
            {
                // For array and cube targets the copy's depth spans the layers; cubes count six faces
                var (levelWidth, levelHeight, levelDepth) = MaterialLoader.GetChainLevelSize(target, stream.AllocWidth, stream.AllocHeight, stream.AllocDepth, c);

                GL.CopyImageSubData(
                    texture.Handle, (ImageTarget)target, c - stream.ResidentChainLevel, 0, 0, 0,
                    newHandle, (ImageTarget)target, c - chainLevel, 0, 0, 0,
                    levelWidth, levelHeight, levelDepth);
            }

            texture.ReplaceHandle(newHandle, levels);
            stream.ResidentChainLevel = chainLevel;
        }

        /// <summary>One frame's slice of streaming work: issues the load requests it can and hooks up the
        /// bits that have arrived, advancing each texture's chain to its next mip. Must be called on a
        /// thread with a GL context, once per frame.</summary>
        /// <param name="frameTime">Duration of the previous frame in seconds. The slice gets 20% of it, less
        /// what the previous slice spent, so its own cost never feeds back into its budget.</param>
        public void Timeslice(float frameTime = 1f / 60f)
        {
            var start = Stopwatch.GetTimestamp();
            var budgetSeconds = Math.Max(0f, frameTime * 0.2f - (float)lastSliceDuration / Stopwatch.Frequency);

            RunUntil(deadline: start + (long)(budgetSeconds * Stopwatch.Frequency));

            lastSliceDuration = Stopwatch.GetTimestamp() - start;
        }

        /// <summary>Body of a slice, run until the queues run dry or the deadline passes. Returns whether
        /// any bits were hooked up.</summary>
        private bool RunUntil(long deadline)
        {
            var hookedUp = false;

            // Start newly loaded textures and restart chains parked by the throttle
            while (Interlocked.Read(ref bitsInFlight) < MaxBitsInFlight && pendingLoadRequests.TryDequeue(out var stream))
            {
                IssueLoadRequest(stream);
            }

            // Otherwise captures carry a permanent zero-cost debug group
            if (!loadedMipBits.IsEmpty)
            {
                using var _ = new GLDebugGroup("Texture Mip Uploads");

                while (loadedMipBits.TryDequeue(out var bits))
                {
                    Interlocked.Add(ref bitsInFlight, -bits.ByteSize);

                    var stream = bits.Stream;

                    // Texture deleted while the load was in flight; the chain ends here
                    if (stream.Texture.Handle == 0)
                    {
                        ArrayPool<byte>.Shared.Return(bits.Buffer);
                        RetireStream(stream);
                        continue;
                    }

                    HookUpMipBits(in bits);
                    hookedUp = true;

                    // The GL upload copies out of the buffer before returning, so it is dead here
                    ArrayPool<byte>.Shared.Return(bits.Buffer);

                    // Hooking the bits up is what lets the chain forward, so the slice paces the pipeline
                    stream.NextMip += bits.MipCount;

                    if (stream.NextMip < stream.Mips.Length)
                    {
                        IssueLoadRequest(stream);
                    }
                    else
                    {
                        RetireStream(stream);
                    }

                    if (Stopwatch.GetTimestamp() >= deadline)
                    {
                        break;
                    }
                }
            }

            return hookedUp;
        }

        /// <summary>How long the previous <see cref="Timeslice"/> took, in <see cref="Stopwatch"/> ticks.</summary>
        private long lastSliceDuration;

        /// <summary>Whether any streamed texture still has mips waiting to be requested, in flight on a
        /// load job, or awaiting hookup. While true, <see cref="Timeslice"/> still has work to do.</summary>
        private bool HasPendingWork
            => !pendingLoadRequests.IsEmpty || !loadedMipBits.IsEmpty || Interlocked.Read(ref bitsInFlight) > 0;

        /// <summary>Runs until every stream has finished, for one-shot consumers that have no frame loop
        /// to slice against. Unbudgeted, since there is no frame to stay inside of, and it backs off
        /// whenever it comes up empty: the loads are still in flight on the thread pool, and spinning here
        /// only takes cores away from them. Must be called on a thread with a GL context.</summary>
        public void FinishAllStreaming(CancellationToken cancellationToken = default)
        {
            var backoff = new SpinWait();

            while (HasPendingWork && !cancellationToken.IsCancellationRequested)
            {
                if (RunUntil(deadline: long.MaxValue))
                {
                    backoff = new SpinWait();
                }
                else
                {
                    backoff.SpinOnce();
                }
            }
        }

        /// <summary>Dispatches the load for a stream's next mip, or parks the stream for a later slice when
        /// too many bytes are in flight. Only called on the slice's thread, so the throttle check never races its own adds.</summary>
        private void IssueLoadRequest(StreamedTexture stream)
        {
            // A parked chain that was finished inline by a non-streaming request has nothing left to do
            if (stream.NextMip >= stream.Mips.Length)
            {
                return;
            }

            if (Interlocked.Read(ref bitsInFlight) >= MaxBitsInFlight)
            {
                pendingLoadRequests.Enqueue(stream);
                return;
            }

            // Counted from load request to hookup, so the throttle bounds rented buffers, not just queued data
            Interlocked.Add(ref bitsInFlight, PendingLoadBytes(stream));

            stream.Started = true;
            ThreadPool.UnsafeQueueUserWorkItem(stream, preferLocal: false);
        }

        /// <summary>Data bytes the stream's next load request holds of the throttle: one mip, or the whole
        /// remaining chain. Also the load's unwind amount on failure, so both compute it here.</summary>
        private int PendingLoadBytes(StreamedTexture stream)
        {
            if (!LoadWholeChains)
            {
                return stream.Mips[stream.NextMip].BufferSize;
            }

            var total = 0;

            for (var i = stream.NextMip; i < stream.Mips.Length; i++)
            {
                total += stream.Mips[i].BufferSize;
            }

            return total;
        }

        /// <summary>Finishes a texture's mip chain inline, so callers that copy or measure the texture at
        /// load time — cookie atlases most of all — never see the growing stub a material left behind.
        /// Chains cannot have started before the render loop's first slice, which is where every load-time
        /// caller runs, so the remaining mips can be loaded and hooked up on the spot.</summary>
        internal void FinishStreaming(RenderTexture tex)
        {
            if (!incompleteStreams.TryRemove(tex, out var stream))
            {
                return;
            }

            if (stream.Started)
            {
                // A started chain has a load in flight whose bookkeeping an inline finish would race.
                // Load-time callers cannot get here; leave the chain to the slices.
                Debug.Assert(false, $"Non-streaming request for {stream.Name} while its chain is already streaming");
                incompleteStreams.TryAdd(tex, stream);
                return;
            }

            while (stream.NextMip < stream.Mips.Length)
            {
                LoadAndHookUpMip(stream, stream.Mips[stream.NextMip]);
                stream.NextMip++;
            }

            // The stream may still sit in the pending queue; a later slice discards finished chains
            RetireStream(stream);
        }

        /// <summary>Loads one mip synchronously and hooks its bits up on the spot.</summary>
        internal static void LoadAndHookUpMip(StreamedTexture stream, in PlannedMip mip)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(stream.InPlaceSize(mip));

            try
            {
                lock (stream.Data)
                {
                    stream.Data.ReadTextureMipLevelInPlace(buffer, stream.DataMipLevel(mip));
                }

                HookUpMipBits(new LoadedMipBits(stream, mip, MipCount: 1, mip.BufferSize, buffer));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>Serves one load request: reads the stream's next mip — or its whole remaining
        /// chain — into one pooled buffer, each mip decoded in place at its own offset, and queues the
        /// bits for hookup.</summary>
        internal void LoadStreamingData(StreamedTexture stream)
        {
            var first = stream.NextMip;
            var count = LoadWholeChains ? stream.Mips.Length - first : 1;
            var gateBytes = PendingLoadBytes(stream);

            // Owned by this method until the enqueue hands it to the hookup; any exit before that returns it
            byte[]? buffer = null;

            try
            {
                var total = 0;

                for (var i = 0; i < count; i++)
                {
                    total += stream.InPlaceSize(stream.Mips[first + i]);
                }

                // Returned to the pool once the hookup has handed the data to the driver
                buffer = ArrayPool<byte>.Shared.Rent(total);

                var offset = 0;

                // The srgb and non-srgb variants of one resource share the Texture block and its reader
                lock (stream.Data)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var mip = stream.Mips[first + i];
                        var size = stream.InPlaceSize(mip);

                        stream.Data.ReadTextureMipLevelInPlace(buffer.AsSpan(offset, size), stream.DataMipLevel(mip));
                        offset += size;
                    }
                }

                loadedMipBits.Enqueue(new LoadedMipBits(stream, stream.Mips[first], count, gateBytes, buffer));
                buffer = null; // ownership transferred to the hookup
            }
            catch (Exception e)
            {
                // An exception out of an IThreadPoolWorkItem takes the process down. The chain ends
                // here: revealing anything past a failed level would expose undefined contents.
                rendererContext.Logger.LogError(e, "Loading mips of {Texture} failed", stream.Name);
                Interlocked.Add(ref bitsInFlight, -gateBytes);
                RetireStream(stream);
            }
            finally
            {
                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
    }
}
