using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Materials
{
    public partial class MaterialLoader
    {
        /// <summary>Streams waiting to start their next mip read: newly loaded textures and chains parked by the in-flight byte gate.</summary>
        private readonly ConcurrentQueue<StreamedTexture> pendingStreams = new();

        /// <summary>Read mip data waiting for the pump. A single FIFO queue: reads are dispatched
        /// sequentially per texture, so it preserves each texture's chain order by construction.</summary>
        private readonly ConcurrentQueue<MipUploadData> pendingMipUploads = new();

        /// <summary>Ceiling on mip buffer bytes in flight between dispatch and upload. Chains park at the
        /// cap and the upload pump resumes them, which bounds the working set the shared array pool must
        /// cover and keeps the managed heap bounded while the render loop is not running yet.</summary>
        private const long MaxPendingUploadBytes = 256L * 1024 * 1024;

        /// <summary>Buffer bytes between dispatch and upload. A soft gate, adjusted with interlocked adds by the pump and failed reads.</summary>
        private long pendingUploadBytes;

        /// <summary>When set, a dispatched read job reads its texture's whole remaining chain into one
        /// buffer and the pump applies it with a single storage recreation — one texture per worker,
        /// batched like the synchronous path but with textures in parallel. For one-shot consumers that
        /// drain to completion, like the thumbnail renderer; the scene render loop stays per-mip, whose
        /// small work items its frame budget needs.</summary>
        public bool ReadWholeChains { get; set; }

        /// <summary>Incomplete streams by their texture, so a non-streaming request for a texture that a
        /// material already started streaming can finish the chain inline instead of sampling a stub.</summary>
        private readonly ConcurrentDictionary<RenderTexture, StreamedTexture> incompleteStreams = new();

        /// <summary>Retires a chain at any of its terminal points: completed, failed, or dropped with its
        /// texture. Idempotent — a retired chain can still be dequeued from the pending queue by a later drain.</summary>
        private void RetireStream(StreamedTexture stream)
        {
            incompleteStreams.TryRemove(stream.Texture, out _);
        }

        /// <summary>Drops all pending stream work, the counterpart to <see cref="FinishAllStreaming"/>.
        /// Makes no GL calls, so it is also safe during teardown without a current context. The byte gate
        /// is unwound per item rather than zeroed: a straggling read's bytes stay counted until its upload
        /// surfaces, so the counter never goes negative.</summary>
        public void CancelAllStreaming()
        {
            while (pendingStreams.TryDequeue(out var stream))
            {
                RetireStream(stream);
            }

            // Drain rather than clear: these buffers belong to the pool, not the garbage collector
            while (pendingMipUploads.TryDequeue(out var upload))
            {
                ArrayPool<byte>.Shared.Return(upload.Buffer);
                Interlocked.Add(ref pendingUploadBytes, -upload.ByteSize);
                RetireStream(upload.Stream);
            }

            incompleteStreams.Clear();
        }

        /// <summary>Applies one read mip level: recreates the storage one level larger, copying the
        /// resident smaller levels over, then sub-images the new mip into the new top level. Uploads of
        /// one texture arrive in chain order, smallest mip first, so the storage only ever holds defined
        /// levels — nothing can sample as black — and VRAM stays tightly packed to what has arrived.</summary>
        private static void ApplyUpload(in MipUploadData upload)
        {
            var stream = upload.Stream;
            var texture = stream.Texture;

            if (texture.Handle == 0)
            {
                return; // deleted while the read was in flight; drop the data
            }

            Debug.Assert(upload.MipCount == 1 || stream.Mips[stream.NextMip] == upload.Mip);

            // Grow once, to the largest mip in the batch; level indices then count from the new top
            var lastMip = upload.MipCount == 1 ? upload.Mip : stream.Mips[stream.NextMip + upload.MipCount - 1];
            GrowStorage(stream, lastMip.ChainLevel);

            var offset = 0;

            for (var i = 0; i < upload.MipCount; i++)
            {
                var mip = i == 0 ? upload.Mip : stream.Mips[stream.NextMip + i];

                SubImageMip(stream, mip.ChainLevel - stream.TopChainLevel, mip, upload.Buffer, offset);
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

        /// <summary>Dimensions of one chain level for any texture target: width always halves per level,
        /// height is spatial except for 1D arrays where it carries the layer count, and depth halves only
        /// for volumes — for array and cube targets it carries the layer (times face) count.</summary>
        private static (int Width, int Height, int Depth) GetChainLevelSize(TextureTarget target, int width, int height, int depth, int chainLevel)
        {
            var levelWidth = Math.Max(1, width >> chainLevel);

            var levelHeight = target is TextureTarget.Texture1D or TextureTarget.Texture1DArray
                ? height
                : Math.Max(1, height >> chainLevel);

            var levelDepth = target is TextureTarget.Texture3D
                ? Math.Max(1, depth >> chainLevel)
                : depth;

            return (levelWidth, levelHeight, levelDepth);
        }

        /// <summary>Allocates immutable storage on a texture object, dispatching to the storage call the target requires.</summary>
        private static void CreateStorageForTarget(int handle, TextureTarget target, int levels, SizedInternalFormat format, int width, int height, int depth)
        {
            switch (target)
            {
                case TextureTarget.Texture1D:
                    GL.TextureStorage1D(handle, levels, format, width);
                    break;

                // Cube storage allocates its six faces from 2D dimensions; a 1D array's layers ride in height
                case TextureTarget.Texture1DArray:
                case TextureTarget.Texture2D:
                case TextureTarget.TextureCubeMap:
                    GL.TextureStorage2D(handle, levels, format, width, height);
                    break;

                default:
                    GL.TextureStorage3D(handle, levels, format, width, height, depth);
                    break;
            }
        }

        /// <summary>Recreates a streamed texture's storage sized for the given chain level, copies the
        /// resident smaller levels into its tail and swaps the texture over to the new object. No-op for
        /// levels the current storage already covers, such as the synchronous smallest mip at load.</summary>
        private static void GrowStorage(StreamedTexture stream, int chainLevel)
        {
            if (chainLevel >= stream.TopChainLevel)
            {
                return;
            }

            var texture = stream.Texture;
            var target = texture.Target;
            var levels = stream.ChainLevels - chainLevel;
            var (width, height, depth) = GetChainLevelSize(target, stream.AllocWidth, stream.AllocHeight, stream.AllocDepth, chainLevel);

            var newHandle = GraphicsDevice.CreateTexture(target, stream.Name);
            CreateStorageForTarget(newHandle, target, levels, stream.SizedInternalFormat, width, height, depth);

            for (var c = stream.TopChainLevel; c < stream.ChainLevels; c++)
            {
                // For array and cube targets the copy's depth spans the layers; cubes count six faces
                var (levelWidth, levelHeight, levelDepth) = GetChainLevelSize(target, stream.AllocWidth, stream.AllocHeight, stream.AllocDepth, c);

                GL.CopyImageSubData(
                    texture.Handle, (ImageTarget)target, c - stream.TopChainLevel, 0, 0, 0,
                    newHandle, (ImageTarget)target, c - chainLevel, 0, 0, 0,
                    levelWidth, levelHeight, levelDepth);
            }

            texture.ReplaceHandle(newHandle, levels);
            stream.TopChainLevel = chainLevel;
        }

        /// <summary>Starts pending mip reads and applies mip data the reads have produced, advancing each
        /// texture's chain to its next mip. Must be called on a thread with a GL context, once per frame.</summary>
        /// <param name="frameTime">Duration of the previous frame in seconds. Uploads get 20% of it, less
        /// what the previous pump already spent, so the pump's own cost never feeds back into its budget.</param>
        public void UploadPendingTextures(float frameTime = 1f / 60f)
        {
            var start = Stopwatch.GetTimestamp();
            var budgetSeconds = Math.Max(0f, frameTime * 0.2f - (float)lastUploadDuration / Stopwatch.Frequency);

            Pump(deadline: start + (long)(budgetSeconds * Stopwatch.Frequency));

            lastUploadDuration = Stopwatch.GetTimestamp() - start;
        }

        /// <summary>Body of the pump: starts what reads it can and applies what has arrived, until the
        /// queue runs dry or the deadline passes. Returns whether any upload was applied.</summary>
        private bool Pump(long deadline)
        {
            var applied = false;

            // Start newly loaded textures and restart chains parked by the byte gate
            while (Interlocked.Read(ref pendingUploadBytes) < MaxPendingUploadBytes && pendingStreams.TryDequeue(out var stream))
            {
                StartNextRead(stream);
            }

            // Otherwise captures carry a permanent zero-cost debug group
            if (!pendingMipUploads.IsEmpty)
            {
                using var _ = new GLDebugGroup("Texture Mip Uploads");

                while (pendingMipUploads.TryDequeue(out var upload))
                {
                    Interlocked.Add(ref pendingUploadBytes, -upload.ByteSize);

                    var stream = upload.Stream;

                    // Texture deleted while the read was in flight; the chain ends here
                    if (stream.Texture.Handle == 0)
                    {
                        ArrayPool<byte>.Shared.Return(upload.Buffer);
                        RetireStream(stream);
                        continue;
                    }

                    ApplyUpload(in upload);
                    applied = true;

                    // The GL upload copies out of the buffer before returning, so it is dead here
                    ArrayPool<byte>.Shared.Return(upload.Buffer);

                    // The applied upload is what lets the chain forward, so this pump paces the pipeline
                    stream.NextMip += upload.MipCount;

                    if (stream.NextMip < stream.Mips.Length)
                    {
                        StartNextRead(stream);
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

            return applied;
        }

        /// <summary>How long the previous <see cref="UploadPendingTextures"/> took, in <see cref="Stopwatch"/> ticks.</summary>
        private long lastUploadDuration;

        /// <summary>Whether any streamed texture still has mips waiting to start, in flight on a read job,
        /// or awaiting upload. While true, <see cref="UploadPendingTextures"/> still has work to do.</summary>
        private bool HasPendingTextureStreams
            => !pendingStreams.IsEmpty || !pendingMipUploads.IsEmpty || Interlocked.Read(ref pendingUploadBytes) > 0;

        /// <summary>Pumps until every stream has finished, for one-shot consumers that have no frame
        /// loop to pump later. Runs the pump unbudgeted, since there is no frame to stay inside of, and
        /// backs off whenever it comes up empty: the reads are still in flight on the thread pool, and
        /// spinning here only takes cores away from them. Must be called on a thread with a GL context.</summary>
        public void FinishAllStreaming(CancellationToken cancellationToken = default)
        {
            var backoff = new SpinWait();

            while (HasPendingTextureStreams && !cancellationToken.IsCancellationRequested)
            {
                if (Pump(deadline: long.MaxValue))
                {
                    backoff = new SpinWait();
                }
                else
                {
                    backoff.SpinOnce();
                }
            }
        }

        /// <summary>Dispatches the read for a stream's next mip, or parks the stream for a later pump when
        /// too many bytes are in flight. Only called on the pump's thread, so the gate check never races its own adds.</summary>
        private void StartNextRead(StreamedTexture stream)
        {
            // A parked chain that was finished inline by a non-streaming request has nothing left to do
            if (stream.NextMip >= stream.Mips.Length)
            {
                return;
            }

            if (Interlocked.Read(ref pendingUploadBytes) >= MaxPendingUploadBytes)
            {
                pendingStreams.Enqueue(stream);
                return;
            }

            // Counted from dispatch to upload, so the gate bounds rented buffers, not just queued data
            Interlocked.Add(ref pendingUploadBytes, PendingReadBytes(stream));

            stream.Started = true;
            ThreadPool.UnsafeQueueUserWorkItem(stream, preferLocal: false);
        }

        /// <summary>Data bytes the stream's next read holds of the byte gate: one mip, or the whole
        /// remaining chain. Also the read's unwind amount on failure, so both compute it here.</summary>
        private int PendingReadBytes(StreamedTexture stream)
        {
            if (!ReadWholeChains)
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
        /// Chains cannot have started before the render loop's first pump, which is where every load-time
        /// caller runs, so the remaining mips can be read and applied on the spot.</summary>
        private void FinishStreaming(RenderTexture tex)
        {
            if (!incompleteStreams.TryRemove(tex, out var stream))
            {
                return;
            }

            if (stream.Started)
            {
                // A started chain has a read in flight whose bookkeeping an inline finish would race.
                // Load-time callers cannot get here; leave the chain to the pump.
                Debug.Assert(false, $"Non-streaming request for {stream.Name} while its chain is already streaming");
                incompleteStreams.TryAdd(tex, stream);
                return;
            }

            while (stream.NextMip < stream.Mips.Length)
            {
                ReadAndApplyMip(stream, stream.Mips[stream.NextMip]);
                stream.NextMip++;
            }

            // The stream may still sit in the pending queue; the pump discards finished chains
            RetireStream(stream);
        }

        /// <summary>Reads one mip synchronously and applies its upload on the spot.</summary>
        private static void ReadAndApplyMip(StreamedTexture stream, in PlannedMip mip)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(stream.InPlaceSize(mip));

            try
            {
                lock (stream.Data)
                {
                    stream.Data.ReadTextureMipLevelInPlace(buffer, stream.DataMipLevel(mip));
                }

                ApplyUpload(new MipUploadData(stream, mip, MipCount: 1, mip.BufferSize, buffer));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>Reads a stream's next mip — or its whole remaining chain — into one
        /// pooled buffer, each mip decoded in place at its own offset, and queues it for the pump.</summary>
        internal void ExecuteRead(StreamedTexture stream)
        {
            var first = stream.NextMip;
            var count = ReadWholeChains ? stream.Mips.Length - first : 1;
            var gateBytes = PendingReadBytes(stream);

            // Owned by this method until the enqueue hands it to the pump; any exit before that returns it
            byte[]? buffer = null;

            try
            {
                var total = 0;

                for (var i = 0; i < count; i++)
                {
                    total += stream.InPlaceSize(stream.Mips[first + i]);
                }

                // Returned to the pool by the upload pump once the data is handed to the driver
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

                pendingMipUploads.Enqueue(new MipUploadData(stream, stream.Mips[first], count, gateBytes, buffer));
                buffer = null; // ownership transferred to the pump
            }
            catch (Exception e)
            {
                // An exception out of an IThreadPoolWorkItem takes the process down. The chain ends
                // here: revealing anything past a failed level would expose undefined contents.
                RendererContext.Logger.LogError(e, "Reading mips of {Texture} failed", stream.Name);
                Interlocked.Add(ref pendingUploadBytes, -gateBytes);
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
