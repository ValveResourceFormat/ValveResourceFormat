using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>
    /// Reads texture mip data on background threads and gives it to the GPU over the frames that follow,
    /// so load time stops scaling with how much texture data a scene uses. A streamed texture starts out
    /// holding only its smallest mip and grows one level at a time as data arrives.
    /// Vocabulary follows Source 2's <c>CTextureStreamingHelper</c>: mip data is "bits", asking for a
    /// texture's next mip is a "load request", and the per-frame work is "time sliced".
    /// </summary>
    public sealed class TextureStreamingHelper(RendererContext rendererContext)
    {
        // Streams waiting to ask for their next mip: new textures, and chains parked by the throttle
        private readonly ConcurrentQueue<StreamedTexture> pendingLoadRequests = new();

        // One FIFO for everything: a texture only has one request out at a time, so this is already in chain order
        private readonly ConcurrentQueue<LoadedMipBits> loadedMipBits = new();

        // Streams by texture, so a caller that needs a texture whole can finish its chain inline
        private readonly ConcurrentDictionary<RenderTexture, StreamedTexture> incompleteStreams = new();

        // Cap on buffer bytes between a load request and its hookup. Chains park at the cap until a later
        // slice drains them, which bounds both the pool's working set and the heap before the render loop runs.
        private const long MaxBitsInFlight = 256L * 1024 * 1024;

        private long bitsInFlight;

        private long lastSliceDuration;

        /// <summary>
        /// False: parallelize single mips; True: parallelize whole textures.
        /// </summary>
        public bool LoadWholeChains { get; set; }

        internal void BeginStreaming(StreamedTexture stream)
        {
            incompleteStreams.TryAdd(stream.Texture, stream);
            pendingLoadRequests.Enqueue(stream);
        }

        // Ends a chain, however it ended: finished, failed, or its texture went away. Idempotent, because a
        // retired stream can still be sitting in a queue and get dequeued later.
        private void RetireStream(StreamedTexture stream)
        {
            incompleteStreams.TryRemove(stream.Texture, out _);
        }

        /// <summary>
        /// Throws away everything still streaming and hands its buffers back to the pool. Makes no GL calls,
        /// so it is safe during teardown with no current context.
        /// </summary>
        public void CancelAllStreaming()
        {
            while (pendingLoadRequests.TryDequeue(out var stream))
            {
                RetireStream(stream);
            }

            // Drain rather than clear: these buffers belong to the pool, not the garbage collector. The byte
            // count comes off per item, never zeroed, so a load still in flight keeps its bytes counted.
            while (loadedMipBits.TryDequeue(out var bits))
            {
                ArrayPool<byte>.Shared.Return(bits.Buffer);
                Interlocked.Add(ref bitsInFlight, -bits.ByteSize);
                RetireStream(bits.Stream);
            }

            incompleteStreams.Clear();
        }

        // Grows the storage to fit the new mips, then uploads them. A texture's bits always arrive smallest
        // first, so every level in the storage has been written and nothing can ever sample as black.
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

        // Immutable storage cannot grow, so bigger levels mean a new texture object: allocate one, copy the
        // levels already there into its tail, and swap the handle over. VRAM stays packed to what arrived.
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

        /// <summary>
        /// Does one frame's worth of streaming: starts the loads it can, and gives the GPU whatever mip data
        /// has arrived since last time. Call once per frame, on a thread with a GL context.
        /// </summary>
        /// <param name="frameTime">Length of the previous frame in seconds. This call gets 20% of it, minus
        /// what the previous call spent, so its own cost cannot grow its budget.</param>
        public void Timeslice(float frameTime = 1f / 60f)
        {
            var start = Stopwatch.GetTimestamp();
            var budgetSeconds = Math.Max(0f, frameTime * 0.2f - (float)lastSliceDuration / Stopwatch.Frequency);

            RunUntil(deadline: start + (long)(budgetSeconds * Stopwatch.Frequency));

            lastSliceDuration = Stopwatch.GetTimestamp() - start;
        }

        // The work itself, until the queues run dry or the deadline passes. Says whether it hooked anything up.
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

                    // Only a hookup moves a chain forward, which is what paces the whole pipeline
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

        // Anything left to request, in flight, or waiting to be hooked up
        private bool HasPendingWork
            => !pendingLoadRequests.IsEmpty || !loadedMipBits.IsEmpty || Interlocked.Read(ref bitsInFlight) > 0;

        /// <summary>
        /// Keeps working until nothing is left to stream, for when there is no frame loop to spread the work
        /// over. Runs to no budget, and sleeps rather than spins while it waits, since the loads are running
        /// on the thread pool and spinning here would only take threads away from them. Must be called on a
        /// thread with a GL context.
        /// </summary>
        /// <param name="cancellationToken">Stops early, leaving chains unfinished.</param>
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

        // Asks for a stream's next mip, or parks it until the throttle lets up. Only ever runs on the thread
        // doing the slice, so reading the byte count cannot race the adds made right below it.
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

            // Counted from the request, not from the enqueue, so the throttle bounds rented buffers too
            Interlocked.Add(ref bitsInFlight, PendingLoadBytes(stream));

            stream.Started = true;
            ThreadPool.UnsafeQueueUserWorkItem(stream, preferLocal: false);
        }

        // What the next request costs the throttle, and what a failed one has to give back
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

        // Finishes a chain on the spot, for a caller that needs the whole texture rather than the stub a
        // material left growing. Only works before the chain started, which no chain has by the time the
        // first slice runs, so this is a rule rather than a fallback. Returns true if texture is whole.
        internal bool FinishStreaming(RenderTexture tex)
        {
            if (!incompleteStreams.TryRemove(tex, out var stream))
            {
                return true;
            }

            if (stream.Started)
            {
                // A started chain has a load in flight whose bookkeeping this would race
                Debug.Assert(false, $"Non-streaming request for {stream.Name} while its chain is already streaming");
                incompleteStreams.TryAdd(tex, stream);
                return false;
            }

            while (stream.NextMip < stream.Mips.Length)
            {
                LoadAndHookUpMip(stream, stream.Mips[stream.NextMip]);
                stream.NextMip++;
            }

            // The stream may still sit in the pending queue; a later slice discards finished chains
            RetireStream(stream);

            return true;
        }

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

        // Serves one load request, on a thread pool thread: reads the mips it was asked for into a single
        // pooled buffer, each decoded in place at its own offset, and queues them to be hooked up.
        internal void LoadStreamingData(StreamedTexture stream)
        {
            var first = stream.NextMip;
            var count = LoadWholeChains ? stream.Mips.Length - first : 1;
            var gateBytes = PendingLoadBytes(stream);

            // Owned here until the enqueue hands it over; any exit before that returns it
            byte[]? buffer = null;

            try
            {
                var total = 0;

                for (var i = 0; i < count; i++)
                {
                    total += stream.InPlaceSize(stream.Mips[first + i]);
                }

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
                buffer = null; // handed over
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
