using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>How texture mip data reaches the GPU.</summary>
    public enum TextureStreamingMode
    {
        /// <summary>Read in the background, arriving over the frames after load.</summary>
        Timesliced,

        /// <summary>Read in the background, but complete before the next frame is drawn.</summary>
        Immediate,

        /// <summary>Read whole on the thread that asks for the texture, using no background threads.</summary>
        Synchronous,
    }

    /// <summary>
    /// Reads texture mip data on background threads and gives it to the GPU over the frames that follow.
    /// A streamed texture starts out holding only its smallest mip. A texture materials draw then grows to
    /// the resolution the closest visible mesh drawing it needs, measured by <see cref="BeginDemandPass"/>, and gives
    /// back mips when video memory runs out. A texture that no mesh draw call uses, such as one a particle
    /// draws, grows to its whole chain.
    /// </summary>
    public sealed class TextureStreamingHelper(RendererContext rendererContext)
    {
        private readonly ConcurrentQueue<StreamedTexture> pendingLoadRequests = new();

        private const long VideoMemoryReserve = 256L * 1024 * 1024;

        private bool loggedLowVideoMemory;

        // A texture only has one request out at a time, so this is already in chain order
        private readonly ConcurrentQueue<LoadedMipBits> loadedMipBits = new();

        // Filled from loading threads, joins the list the slices own on the next pass
        private readonly ConcurrentQueue<StreamedTexture> newStreams = new();

        private readonly List<StreamedTexture> streams = [];
        private readonly Lock streamsLock = new();

        // Cap on buffer bytes between a load request and its hookup, enough for a few frames of uploads
        private const long MaxBitsInFlight = 64L * 1024 * 1024;

        // Requests read into it and upload from it; one too big for it reads into a pooled array
        private readonly StagingRing staging = new("Texture Streaming Staging", (int)MaxBitsInFlight);

        private long bitsInFlight;

        // While set, reads that finish hand their buffer back instead of queueing an upload
        private volatile bool suspended;

        private long lastSliceDuration;

        private const int MaxRequestsPerSlice = 16;
        private const long MaxRequestBytesPerSlice = 32L * 1024 * 1024;

        private const float DemandIntervalSeconds = 0.25f;
        private const float DemandHalfLifeSeconds = 5f;
        private const int MaxShrinksPerSlice = 32;

        private long lastDemandTimestamp;
        private float demandDecay;
        private Vector3 demandEye;
        private bool demandOrthographic;
        private float demandPixelsPerUnitAtUnitDistance;
        private float demandPixelsPerModelUnit;
        private SceneNode? demandNode;

        private readonly List<StreamedTexture> loadCandidates = [];

        /// <summary>
        /// How textures load. Safe to change at any point; chains already under way finish either way.
        /// </summary>
        public TextureStreamingMode Mode { get; set; }

        internal void BeginStreaming(StreamedTexture stream) => newStreams.Enqueue(stream);

        /// <summary>
        /// Stops reads from writing to the staging buffer and waits out the ones under way, so the buffer can
        /// go away with the GL context. Makes no GL calls.
        /// </summary>
        public void CancelReads() => staging.Close();

        /// <summary>
        /// Throws away everything queued and hands its buffers back to the pool. Makes no GL calls, so it
        /// is safe during teardown with no current context.
        /// </summary>
        public void CancelAllStreaming()
        {
            DropQueuedWork();

            newStreams.Clear();

            using (streamsLock.EnterScope())
            {
                streams.Clear();
            }
        }

        /// <summary>
        /// Stops loading while nothing is drawing: drops the queued requests and the data waiting to be
        /// uploaded, and drops what reads still in flight deliver. The textures keep what they hold, and
        /// streaming resumes by itself with the next <see cref="Timeslice"/>. Makes no GL calls.
        /// </summary>
        public void Suspend()
        {
            suspended = true;
            DropQueuedWork();
        }

        private void DropQueuedWork()
        {
            while (pendingLoadRequests.TryDequeue(out var stream))
            {
                stream.Queued = false;
            }

            DiscardLoadedMipBits();
        }

        // Drained rather than cleared, as these buffers belong to the pool. The count comes off per item,
        // so a load still in flight keeps its bytes counted.
        private void DiscardLoadedMipBits()
        {
            while (loadedMipBits.TryDequeue(out var bits))
            {
                DiscardBits(in bits);
            }
        }

        private void DiscardBits(in LoadedMipBits bits)
        {
            if (bits.Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(bits.Buffer);
            }

            ReleaseInFlight(bits.Stream, bits.ByteSize);
        }

        // Only the render thread passes uploaded, for a staging region it uploads from
        private void ReleaseInFlight(StreamedTexture stream, long bytes, bool uploaded = false)
        {
            if (stream.Staging is { } region)
            {
                staging.Release(region, uploaded);
                stream.Staging = null;
            }

            Interlocked.Add(ref bitsInFlight, -bytes);
            stream.LoadInFlight = false;
        }

        /// <summary>
        /// Deletes the staging buffer. Call with the GL context current, after the textures are deleted
        /// and streaming is cancelled.
        /// </summary>
        public void DeleteStaging() => staging.Delete();

        /// <summary>
        /// Whether any idle texture holds storage past its smallest mip, which <see cref="TrimStorage"/> would free.
        /// Makes no GL calls.
        /// </summary>
        public bool HasStorageToTrim()
        {
            using var _ = streamsLock.EnterScope();

            DrainNewStreams();

            return streams.Exists(CanTrim);
        }

        /// <summary>
        /// Shrinks every idle texture to its smallest mip, the state it loaded in, so a viewer that is not
        /// drawn does not hold the finer ones. Must be called on a thread with the viewer's GL context.
        /// </summary>
        public void TrimStorage()
        {
            using var _ = streamsLock.EnterScope();

            DrainNewStreams();

            foreach (var stream in streams)
            {
                if (CanTrim(stream))
                {
                    ReplaceStorage(stream, stream.ChainLevels - 1);
                    stream.SmoothedDemandPixels = 0f;
                }
            }
        }

        private static bool CanTrim(StreamedTexture stream)
            => !stream.LoadInFlight && !stream.Queued && !stream.NeedsWholeChain && !stream.Failed
            && stream.Texture.Handle != 0 && stream.AllocatedChainLevel < stream.ChainLevels - 1;

        private void DrainNewStreams()
        {
            while (newStreams.TryDequeue(out var stream))
            {
                streams.Add(stream);
            }
        }

        /// <summary>
        /// Waits for dispatched loads to land, so no thread is left reading a texture resource that the
        /// caller is about to dispose. Makes no GL calls. Never call this on the UI thread.
        /// </summary>
        public void DrainPendingLoads()
        {
            var backoff = new SpinWait();
            var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency;

            while (Interlocked.Read(ref bitsInFlight) > 0)
            {
                DiscardLoadedMipBits();

                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    // Only reachable if the in-flight count stopped balancing. Give up so teardown is not blocked.
                    rendererContext.Logger.LogError("Gave up waiting on {Bytes} streaming bytes", Interlocked.Read(ref bitsInFlight));
                    break;
                }

                backoff.SpinOnce();
            }
        }

        /// <summary>
        /// Starts a measure of the resolution each streamed texture needs, if one is due. It runs a few times
        /// a second, or every frame while textures load immediately. The views drawn this frame then report the
        /// draws they keep through <see cref="BeginDemandView"/> and <see cref="AddDemand"/>, and
        /// <see cref="EndDemandPass"/> points every texture at the mips it needs. Call on a thread with a GL context.
        /// </summary>
        /// <returns>Whether this frame's draws are to be measured.</returns>
        internal bool BeginDemandPass()
        {
            if (Mode == TextureStreamingMode.Synchronous)
            {
                return false;
            }

            var first = lastDemandTimestamp == 0;
            var elapsed = first ? 0f : (float)Stopwatch.GetElapsedTime(lastDemandTimestamp).TotalSeconds;
            var due = first || elapsed >= DemandIntervalSeconds || !newStreams.IsEmpty || Mode == TextureStreamingMode.Immediate;

            if (!due)
            {
                return false;
            }

            lastDemandTimestamp = Stopwatch.GetTimestamp();
            demandDecay = MathF.Pow(2f, -elapsed / DemandHalfLifeSeconds);

            return true;
        }

        /// <summary>
        /// Sets the camera that the draws given to <see cref="AddDemand"/> are seen through, in the space of
        /// the scene they belong to.
        /// </summary>
        internal void BeginDemandView(Camera camera)
        {
            // Pixels a world unit covers at distance 1. An orthographic projection is the same at any distance.
            var projection = camera.ProjectionMatrix;

            demandEye = camera.Location;
            demandOrthographic = projection.M44 == 1f;
            demandPixelsPerUnitAtUnitDistance = projection.M22 * camera.WindowSize.Y * 0.5f;
            demandNode = null;
        }

        /// <summary>
        /// Records how many pixels a draw that survived culling needs of the streamed textures of its material.
        /// </summary>
        internal void AddDemand(SceneNode node, DrawCall drawCall)
        {
            // A node has many draws, and they are given together
            if (node != demandNode)
            {
                var distance = MathF.Sqrt(node.BoundingBox.DistanceSquared(demandEye));
                var pixelsPerUnit = demandOrthographic ? demandPixelsPerUnitAtUnitDistance : demandPixelsPerUnitAtUnitDistance / Math.Max(distance, 1f);

                demandNode = node;
                demandPixelsPerModelUnit = pixelsPerUnit * MathUtils.MaxAxisScale(node.Transform);
            }

            // A mesh without a density needs its textures whole
            var pixels = drawCall.UvDensity > 0f ? drawCall.UvDensity * demandPixelsPerModelUnit : float.PositiveInfinity;

            foreach (var texture in drawCall.Material.Textures.Values)
            {
                if (texture.Stream is { } stream)
                {
                    stream.DemandPixels = Math.Max(stream.DemandPixels, pixels);
                }
            }
        }

        /// <summary>
        /// Ends the measure <see cref="BeginDemandPass"/> started: points every texture at the mips it needs within
        /// the video memory budget, queues loads for those short of them and, over budget, shrinks those past
        /// them. Call before the slice.
        /// </summary>
        internal void EndDemandPass()
        {
            demandNode = null;

            using var _ = streamsLock.EnterScope();

            DrainNewStreams();

            var resident = 0L;
            var wanted = 0L;

            for (var i = streams.Count - 1; i >= 0; i--)
            {
                var stream = streams[i];

                if (stream.Texture.Handle == 0 || stream.Failed)
                {
                    streams[i] = streams[^1];
                    streams.RemoveAt(streams.Count - 1);
                    continue;
                }

                stream.TargetChainLevel = ChooseTarget(stream, demandDecay);
                resident += stream.StorageBytes(stream.AllocatedChainLevel);
                wanted += stream.StorageBytes(stream.TargetChainLevel);
            }

            var free = GLEnvironment.GetAvailableVideoMemory();
            var budget = free == long.MaxValue ? long.MaxValue : resident + free - VideoMemoryReserve;

            if (wanted > budget)
            {
                if (!loggedLowVideoMemory)
                {
                    loggedLowVideoMemory = true;
                    rendererContext.Logger.LogWarning("Texture streaming low memory available ({Free} MB)", (budget - resident) / (1024 * 1024));
                }

                FitTargets(wanted, budget);
            }

            var shrinks = 0;

            foreach (var stream in streams)
            {
                if (stream.TargetChainLevel < stream.ResidentChainLevel)
                {
                    QueueLoad(stream);
                }
                else if (resident > budget && shrinks < MaxShrinksPerSlice && CanShrink(stream))
                {
                    ReplaceStorage(stream, stream.TargetChainLevel);
                    shrinks++;
                }
            }
        }

        private static bool CanShrink(StreamedTexture stream)
            => !stream.LoadInFlight && !stream.Queued && stream.TargetChainLevel > stream.AllocatedChainLevel;

        // Takes one mip off every texture at a time until they all fit
        private void FitTargets(long wanted, long budget)
        {
            while (wanted > budget)
            {
                var before = wanted;

                foreach (var stream in streams)
                {
                    if (stream.NeedsWholeChain || stream.TargetChainLevel >= stream.ChainLevels - 1)
                    {
                        continue;
                    }

                    wanted -= stream.Mips[stream.MipIndex(stream.TargetChainLevel)].BufferSize;
                    stream.TargetChainLevel++;

                    if (wanted <= budget)
                    {
                        return;
                    }
                }

                if (wanted == before)
                {
                    return;
                }
            }
        }

        // The chain level a texture should hold, from the most pixels any mesh needed of it lately
        private static int ChooseTarget(StreamedTexture stream, float decay)
        {
            stream.SmoothedDemandPixels = Math.Max(stream.DemandPixels, stream.SmoothedDemandPixels * decay);
            stream.DemandPixels = 0f;

            if (stream.NeedsWholeChain || !stream.DrawnByMesh)
            {
                return 0;
            }

            // A mesh draws it, but none was measured yet, such as one on a hidden layer
            if (stream.SmoothedDemandPixels == 0f)
            {
                return stream.ResidentChainLevel;
            }

            var level = MathF.Floor(MathF.Log2(Math.Max(stream.AllocWidth, stream.AllocHeight) / stream.SmoothedDemandPixels));
            return (int)Math.Clamp(level, 0f, stream.ChainLevels - 1);
        }

        private void QueueLoad(StreamedTexture stream)
        {
            if (stream.Queued || stream.LoadInFlight)
            {
                return;
            }

            stream.Queued = true;
            pendingLoadRequests.Enqueue(stream);
        }

        // A texture's bits always arrive smallest first, and the base level only reveals the levels that
        // arrived, so nothing can ever sample as black. The source is the mips' address in client memory,
        // or their offset in the bound unpack buffer.
        private static void HookUpMipBits(in LoadedMipBits bits, nint source)
        {
            var stream = bits.Stream;
            var texture = stream.Texture;

            if (texture.Handle == 0)
            {
                return;
            }

            Debug.Assert(bits.MipCount == 1 || stream.Mips[stream.NextMip] == bits.Mip);

            var lastMip = bits.MipCount == 1 ? bits.Mip : stream.Mips[stream.NextMip + bits.MipCount - 1];

            // Made once for the whole way to the target, instead of growing a level at a time: every growth
            // is a new allocation, and the driver does not hand the old ones back
            var storageLevel = Math.Min(lastMip.ChainLevel, stream.TargetChainLevel);

            if (storageLevel < stream.AllocatedChainLevel)
            {
                ReplaceStorage(stream, storageLevel);
            }

            var offset = 0;

            for (var i = 0; i < bits.MipCount; i++)
            {
                var mip = i == 0 ? bits.Mip : stream.Mips[stream.NextMip + i];

                SubImageMip(stream, mip.ChainLevel - stream.AllocatedChainLevel, mip, source + offset);
                offset += mip.BufferSize;
            }

            stream.ResidentChainLevel = lastMip.ChainLevel;
            UpdateBaseLevel(stream);
        }

        private static void SubImageMip(StreamedTexture stream, int level, in PlannedMip mip, nint data)
        {
            var texture = stream.Texture;

            if (stream.Format.IsBlockCompressed())
            {
                GL.CompressedTextureSubImage2D(texture.Handle, level, 0, 0, mip.Width, mip.Height, (PixelFormat)stream.SizedInternalFormat, mip.BufferSize, data);
            }
            else
            {
                GL.TextureSubImage2D(texture.Handle, level, 0, 0, mip.Width, mip.Height, stream.Format.ToGLPixelFormat(), stream.Format.ToGLPixelType(), data);
            }
        }

        private static unsafe void HookUpPinned(in LoadedMipBits bits)
        {
            fixed (byte* data = bits.Buffer)
            {
                HookUpMipBits(in bits, (nint)data);
            }
        }

        // Immutable storage cannot change size, so a different top level means a new texture object
        private static void ReplaceStorage(StreamedTexture stream, int chainLevel)
        {
            var texture = stream.Texture;
            var levels = stream.ChainLevels - chainLevel;
            var width = MathUtils.MipLevelSize(stream.AllocWidth, chainLevel);
            var height = MathUtils.MipLevelSize(stream.AllocHeight, chainLevel);

            var newHandle = GraphicsDevice.CreateTexture(TextureTarget.Texture2D, stream.Name);
            GL.TextureStorage2D(newHandle, levels, stream.SizedInternalFormat, width, height);

            for (var c = Math.Max(stream.ResidentChainLevel, chainLevel); c < stream.ChainLevels; c++)
            {
                var levelWidth = MathUtils.MipLevelSize(stream.AllocWidth, c);
                var levelHeight = MathUtils.MipLevelSize(stream.AllocHeight, c);

                GL.CopyImageSubData(
                    texture.Handle, ImageTarget.Texture2D, c - stream.AllocatedChainLevel, 0, 0, 0,
                    newHandle, ImageTarget.Texture2D, c - chainLevel, 0, 0, 0,
                    levelWidth, levelHeight, 1);
            }

            texture.ReplaceHandle(newHandle, levels);
            stream.AllocatedChainLevel = chainLevel;
            stream.ResidentChainLevel = Math.Max(stream.ResidentChainLevel, chainLevel);
            UpdateBaseLevel(stream);
        }

        // Samples only the levels whose data arrived
        private static void UpdateBaseLevel(StreamedTexture stream)
        {
            var levels = stream.ChainLevels - stream.AllocatedChainLevel;
            stream.Texture.SetBaseMaxLevel(stream.ResidentChainLevel - stream.AllocatedChainLevel, levels - 1);
        }

        /// <summary>
        /// Does one frame's worth of streaming: starts the loads it can, and gives the GPU whatever mip data
        /// has arrived since last time. Call once per frame, after <see cref="EndDemandPass"/>, on a thread
        /// with a GL context.
        /// </summary>
        /// <param name="frameTime">Length of the previous frame in seconds. This call gets 20% of it, minus
        /// what the previous call spent, so its own cost cannot grow its budget.</param>
        public void Timeslice(float frameTime = 1f / 60f)
        {
            var start = Stopwatch.GetTimestamp();
            var budgetSeconds = Math.Max(0f, frameTime * 0.2f - (float)lastSliceDuration / Stopwatch.Frequency);

            suspended = false;
            RunUntil(deadline: start + (long)(budgetSeconds * Stopwatch.Frequency), throttled: true);

            lastSliceDuration = Stopwatch.GetTimestamp() - start;
        }

        // Textures with the most mips to get go first, then the older ones
        private static int CompareLoadPriority(StreamedTexture a, StreamedTexture b)
        {
            var mipsToGetA = a.ResidentChainLevel - a.TargetChainLevel;
            var mipsToGetB = b.ResidentChainLevel - b.TargetChainLevel;

            if (mipsToGetA != mipsToGetB)
            {
                return mipsToGetB.CompareTo(mipsToGetA);
            }

            return a.Texture.Handle.CompareTo(b.Texture.Handle);
        }

        private void IssueQueuedLoads(bool throttled)
        {
            while (pendingLoadRequests.TryDequeue(out var stream))
            {
                loadCandidates.Add(stream);
            }

            loadCandidates.Sort(CompareLoadPriority);

            var requests = 0;
            var requestBytes = 0L;
            var stagingFull = false;

            foreach (var stream in loadCandidates)
            {
                var throttleReached = throttled && (requests >= MaxRequestsPerSlice || requestBytes >= MaxRequestBytesPerSlice);

                if (throttleReached || stagingFull || Interlocked.Read(ref bitsInFlight) >= MaxBitsInFlight)
                {
                    pendingLoadRequests.Enqueue(stream);
                    continue;
                }

                var budget = throttled ? (int)(MaxRequestBytesPerSlice - requestBytes) : int.MaxValue;
                var issuedBytes = IssueLoadRequest(stream, budget);

                // Smaller requests behind it would keep taking the room it waits for
                if (issuedBytes == StagingFull)
                {
                    stagingFull = true;
                    QueueLoad(stream);
                    continue;
                }

                if (issuedBytes > 0)
                {
                    requests++;
                    requestBytes += issuedBytes;
                }
            }

            loadCandidates.Clear();
        }

        private bool RunUntil(long deadline, bool throttled)
        {
            IssueQueuedLoads(throttled);

            // Otherwise captures carry a permanent zero-cost debug group
            if (loadedMipBits.IsEmpty)
            {
                return false;
            }

            using var _ = new GLDebugGroup("Texture Mip Uploads");

            var hookedUp = false;

            while (loadedMipBits.TryDequeue(out var bits))
            {
                var stream = bits.Stream;

                // Texture deleted while the load was in flight; the chain ends here
                if (stream.Texture.Handle == 0)
                {
                    DiscardBits(in bits);
                    continue;
                }

                Upload(in bits);
                hookedUp = true;

                // Only a hookup moves a chain forward, which is what paces the whole pipeline
                if (stream.ResidentChainLevel > stream.TargetChainLevel)
                {
                    QueueLoad(stream);
                }

                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    break;
                }
            }

            staging.Fence();

            return hookedUp;
        }

        private void Upload(in LoadedMipBits bits)
        {
            if (bits.Buffer is { } buffer)
            {
                // The GL upload copies out of client memory before returning, so the buffer is dead after it
                HookUpPinned(in bits);
                ArrayPool<byte>.Shared.Return(buffer);
            }
            else if (bits.Stream.Staging is { } region)
            {
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, staging.Handle);
                HookUpMipBits(in bits, region.Offset);
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            }

            ReleaseInFlight(bits.Stream, bits.ByteSize, uploaded: true);
        }

        private bool HasPendingWork
            => !pendingLoadRequests.IsEmpty || !loadedMipBits.IsEmpty || Interlocked.Read(ref bitsInFlight) > 0;

        /// <summary>
        /// Blocks until every texture holds what it needs, to no budget. Must be called on a thread with a
        /// GL context, after <see cref="EndDemandPass"/>.
        /// </summary>
        /// <param name="cancellationToken">Stops early, leaving chains unfinished.</param>
        public void FinishAllStreaming(CancellationToken cancellationToken = default)
        {
            var backoff = new SpinWait();

            suspended = false;

            while (HasPendingWork && !cancellationToken.IsCancellationRequested)
            {
                if (RunUntil(deadline: long.MaxValue, throttled: false))
                {
                    backoff = new SpinWait();
                }
                else
                {
                    backoff.SpinOnce();
                }
            }
        }

        private const long StagingFull = -1;

        // Returns the bytes requested, or StagingFull. Only ever runs on the thread doing the slice, so
        // reading the in-flight byte count cannot race the adds made right below it.
        private long IssueLoadRequest(StreamedTexture stream, int budget)
        {
            stream.Queued = false;

            if (stream.LoadInFlight || stream.Failed || stream.Texture.Handle == 0)
            {
                return 0;
            }

            PlanRequest(stream, budget);

            if (stream.RequestMipCount == 0)
            {
                return 0;
            }

            var readBytes = stream.InPlaceReadBytes(stream.NextMip, stream.RequestMipCount);

            if (readBytes <= staging.Size)
            {
                if (!staging.TryReserve(readBytes, out var region))
                {
                    return StagingFull;
                }

                stream.Staging = region;
            }

            // Counted from the request, not from the enqueue, so the in-flight limit bounds rented buffers too
            Interlocked.Add(ref bitsInFlight, stream.GateBytes);

            stream.LoadInFlight = true;
            ThreadPool.UnsafeQueueUserWorkItem(stream, preferLocal: false);

            return stream.GateBytes;
        }

        // Always takes at least one mip, or a chain whose next mip is bigger than the budget would never move
        private static void PlanRequest(StreamedTexture stream, int budget)
        {
            var count = 0;
            var bytes = 0;

            for (var i = stream.NextMip; i < stream.Mips.Length && stream.Mips[i].ChainLevel >= stream.TargetChainLevel; i++)
            {
                var size = stream.Mips[i].BufferSize;

                if (count > 0 && bytes + size > budget)
                {
                    break;
                }

                bytes += size;
                count++;
            }

            stream.RequestMipCount = count;
            stream.GateBytes = bytes;
        }

        // Finishes a chain on the spot and keeps it whole from then on. Returns true if the texture is whole.
        internal static bool FinishStreaming(RenderTexture tex)
        {
            if (tex.Stream is not { } stream)
            {
                return true;
            }

            stream.NeedsWholeChain = true;
            stream.TargetChainLevel = 0;

            if (stream.ResidentChainLevel == 0)
            {
                return true;
            }

            if (stream.LoadInFlight)
            {
                // The load in flight has bookkeeping this would race; the chain finishes through the slices
                return false;
            }

            while (stream.ResidentChainLevel > 0)
            {
                LoadAndHookUpMip(stream, stream.Mips[stream.NextMip]);
            }

            return true;
        }

        internal static void LoadAndHookUpMip(StreamedTexture stream, in PlannedMip mip)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(mip.BufferSize);

            try
            {
                lock (stream.Data)
                {
                    stream.Data.ReadTextureMipLevel(buffer.AsSpan(0, mip.BufferSize), stream.DataMipLevel(mip));
                }

                HookUpPinned(new LoadedMipBits(stream, mip, MipCount: 1, mip.BufferSize, buffer));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // Reads the requested mips back to back, each decoded in place, into the staging region reserved for
        // them, or a pooled array when they did not fit the ring
        internal void LoadStreamingData(StreamedTexture stream)
        {
            var first = stream.NextMip;
            var count = stream.RequestMipCount;
            var gateBytes = stream.GateBytes;

            // The request is owned here until the enqueue hands it over; any exit before that gives it up
            byte[]? buffer = null;
            var writing = false;
            var handedOver = false;

            try
            {
                if (stream.Texture.Handle == 0 || suspended)
                {
                    return;
                }

                Span<byte> target;

                if (stream.Staging is { } region)
                {
                    writing = staging.TryBeginWrite(region, out target);

                    if (!writing)
                    {
                        return;
                    }
                }
                else
                {
                    var readBytes = stream.InPlaceReadBytes(first, count);

                    buffer = ArrayPool<byte>.Shared.Rent(readBytes);
                    target = buffer.AsSpan(0, readBytes);
                }

                var offset = 0;

                // The srgb and non-srgb variants of one resource share the Texture block and its reader
                lock (stream.Data)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var mip = stream.Mips[first + i];

                        stream.Data.ReadTextureMipLevelInPlace(target[offset..], stream.DataMipLevel(mip));
                        offset += mip.BufferSize;
                    }
                }

                // Suspended during the read: nothing will upload it
                if (suspended)
                {
                    return;
                }

                loadedMipBits.Enqueue(new LoadedMipBits(stream, stream.Mips[first], count, gateBytes, buffer));
                handedOver = true;
            }
            catch (Exception e) // A corrupt or truncated vtex, or the package disposed under a load still in flight at teardown.
            {
                rendererContext.Logger.LogError(e, "Loading mips of {Texture} failed", stream.Name);
                stream.Failed = true;
            }
            finally
            {
                if (writing)
                {
                    staging.EndWrite();
                }

                if (!handedOver)
                {
                    if (buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    ReleaseInFlight(stream, gateBytes);
                }
            }
        }
    }
}
