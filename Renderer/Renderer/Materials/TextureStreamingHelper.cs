using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;

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
    /// back mips no mesh has needed for a while. A texture that no mesh draw call uses, such as one a particle
    /// draws, grows to its whole chain. A texture a mesh uses that nothing has asked for since it loaded keeps
    /// what it holds.
    /// </summary>
    public sealed class TextureStreamingHelper(RendererContext rendererContext)
    {
        private readonly ConcurrentQueue<StreamedTexture> pendingLoadRequests = new();

        private const long VideoMemoryReserve = 256L * 1024 * 1024;

        private long videoMemoryHeadroom = long.MaxValue;
        private bool loggedLowVideoMemory;

        // A texture only has one request out at a time, so this is already in chain order
        private readonly ConcurrentQueue<LoadedMipBits> loadedMipBits = new();

        // Filled from loading threads, joins the list the slices own on the next pass
        private readonly ConcurrentQueue<StreamedTexture> newStreams = new();

        private readonly List<StreamedTexture> streams = [];
        private readonly Lock streamsLock = new();

        // Cap on buffer bytes between a load request and its hookup.
        private const long MaxBitsInFlight = 256L * 1024 * 1024;

        private long bitsInFlight;

        // While set, reads that finish hand their buffer back instead of queueing an upload
        private volatile bool suspended;

        private long lastSliceDuration;

        private const int MaxRequestsPerSlice = 16;
        private const long MaxRequestBytesPerSlice = 32L * 1024 * 1024;

        private const float DemandIntervalSeconds = 0.25f;

        // The size halves every five seconds, so looking around does not reload mips
        private const float DemandDecayLevelsPerSecond = 0.2f;

        // Shrinking a texture reallocates it, so it only happens when it frees at least this much
        private const long MinShrinkBytes = 64 * 1024;
        private const int MaxShrinksPerSlice = 32;

        // Starts at 1 so a stream no pass has measured, whose DemandPass is 0, never counts as requested
        private int demandPass = 1;
        private long lastDemandTimestamp;
        private long lastTargetsTimestamp;

        private readonly List<StreamedTexture> loadCandidates = [];

        private Vector3 demandEye;
        private bool demandOrthographic;
        private float demandPixelsPerUnitAtUnitDistance;
        private SceneNode? demandNode;
        private float demandPixelsPerUnit;

        /// <summary>
        /// How textures load. Safe to change at any point; chains already under way finish either way.
        /// </summary>
        public TextureStreamingMode Mode { get; set; }

        internal void BeginStreaming(StreamedTexture stream) => newStreams.Enqueue(stream);

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
            ArrayPool<byte>.Shared.Return(bits.Buffer);
            ReleaseInFlight(bits.Stream, bits.ByteSize);
        }

        private void ReleaseInFlight(StreamedTexture stream, long bytes)
        {
            Interlocked.Add(ref bitsInFlight, -bytes);
            stream.LoadInFlight = false;
        }

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
                    stream.SmoothedChainLevel = stream.ChainLevels - 1;
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
        /// a second, or every frame while textures load immediately, and not at all when no texture streams.
        /// The views drawn this frame then report the draws they keep through <see cref="BeginDemandView"/> and
        /// <see cref="AddDemand"/>. Call before the slice, on a thread with a GL context.
        /// </summary>
        /// <returns>Whether this frame's draws are to be measured.</returns>
        internal bool BeginDemandPass()
        {
            if (Mode == TextureStreamingMode.Synchronous)
            {
                return false;
            }

            if (Mode == TextureStreamingMode.Timesliced && newStreams.IsEmpty && lastDemandTimestamp != 0
                && Stopwatch.GetElapsedTime(lastDemandTimestamp).TotalSeconds < DemandIntervalSeconds)
            {
                return false;
            }

            lastDemandTimestamp = Stopwatch.GetTimestamp();
            demandPass++;

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
        /// Records the resolution a draw that survived culling needs of the streamed textures of its material.
        /// </summary>
        internal void AddDemand(SceneNode node, DrawCall drawCall)
        {
            // A node has many draws, and they are given together
            if (node != demandNode)
            {
                var distance = MathF.Sqrt(node.BoundingBox.DistanceSquared(demandEye));

                demandNode = node;
                demandPixelsPerUnit = demandOrthographic ? demandPixelsPerUnitAtUnitDistance : demandPixelsPerUnitAtUnitDistance / Math.Max(distance, 1f);
            }

            // A mesh without a density needs its textures whole
            var pixelsPerUv = drawCall.UvDensity > 0f ? drawCall.UvDensity * demandPixelsPerUnit : float.PositiveInfinity;

            foreach (var texture in drawCall.Material.Textures.Values)
            {
                if (texture.Stream is not { } stream)
                {
                    continue;
                }

                var firstThisPass = stream.DemandPass != demandPass;

                if (firstThisPass || pixelsPerUv > stream.DemandPixelsPerUv)
                {
                    stream.DemandPass = demandPass;
                    stream.DemandPixelsPerUv = pixelsPerUv;
                }
            }
        }

        // The chain level whose texels are about a pixel wide
        private static int DemandedChainLevel(StreamedTexture stream)
        {
            var texelsPerPixel = Math.Max(stream.AllocWidth, stream.AllocHeight) / stream.DemandPixelsPerUv;
            var levelFromSize = MathF.Floor(MathF.Log2(texelsPerPixel));

            return float.IsFinite(levelFromSize) ? (int)Math.Clamp(levelFromSize, 0f, stream.ChainLevels - 1) : 0;
        }

        // Demand snaps to what the last measure asked for when that is finer, and otherwise eases toward
        // coarser levels. A texture the last measure did not ask for can shrink, but not grow.
        private int ChooseTarget(StreamedTexture stream, float elapsedSeconds)
        {
            if (stream.NeedsWholeChain || !stream.DrawnByMesh)
            {
                return 0;
            }

            var requested = stream.DemandPass == demandPass;
            var eased = stream.SmoothedChainLevel + DemandDecayLevelsPerSecond * elapsedSeconds;
            var level = requested ? Math.Min(DemandedChainLevel(stream), eased) : eased;

            level = Math.Min(level, stream.ChainLevels - 1);
            stream.SmoothedChainLevel = level;

            var target = (int)MathF.Floor(level);

            return requested ? target : Math.Max(target, stream.ResidentChainLevel);
        }

        private static long BytesFreedByShrink(StreamedTexture stream, int chainLevel)
        {
            var bytes = 0L;

            for (var level = stream.AllocatedChainLevel; level < chainLevel; level++)
            {
                bytes += stream.Mips[stream.MipIndex(level)].BufferSize;
            }

            return bytes;
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

        private void UpdateTargets(bool throttled)
        {
            suspended = false;

            using var _ = streamsLock.EnterScope();

            DrainNewStreams();

            var now = Stopwatch.GetTimestamp();
            var elapsedSeconds = lastTargetsTimestamp == 0 ? 0f : (float)Stopwatch.GetElapsedTime(lastTargetsTimestamp, now).TotalSeconds;
            lastTargetsTimestamp = now;

            var shrinks = 0;

            for (var i = streams.Count - 1; i >= 0; i--)
            {
                var stream = streams[i];

                if (stream.Texture.Handle == 0 || stream.Failed)
                {
                    streams[i] = streams[^1];
                    streams.RemoveAt(streams.Count - 1);
                    continue;
                }

                var target = ChooseTarget(stream, elapsedSeconds);
                stream.TargetChainLevel = target;

                if (target < stream.ResidentChainLevel)
                {
                    QueueLoad(stream);
                    continue;
                }

                var canShrink = target > stream.AllocatedChainLevel && throttled && !stream.LoadInFlight && !stream.Queued;

                if (canShrink && shrinks < MaxShrinksPerSlice && BytesFreedByShrink(stream, target) >= MinShrinkBytes)
                {
                    ReplaceStorage(stream, target);
                    shrinks++;
                }
            }
        }

        // A texture's bits always arrive smallest first, and the base level only reveals the levels that
        // arrived, so nothing can ever sample as black.
        private static void HookUpMipBits(in LoadedMipBits bits)
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

                SubImageMip(stream, mip.ChainLevel - stream.AllocatedChainLevel, mip, bits.Buffer, offset);
                offset += mip.BufferSize;
            }

            stream.ResidentChainLevel = lastMip.ChainLevel;
            UpdateBaseLevel(stream);
        }

        private static unsafe void SubImageMip(StreamedTexture stream, int level, in PlannedMip mip, byte[] buffer, int offset)
        {
            var texture = stream.Texture;

            fixed (byte* data = &buffer[offset])
            {
                if (stream.Format.IsBlockCompressed())
                {
                    GL.CompressedTextureSubImage2D(texture.Handle, level, 0, 0, mip.Width, mip.Height, (PixelFormat)stream.SizedInternalFormat, mip.BufferSize, (IntPtr)data);
                }
                else
                {
                    GL.TextureSubImage2D(texture.Handle, level, 0, 0, mip.Width, mip.Height, stream.Format.ToGLPixelFormat(), stream.Format.ToGLPixelType(), (IntPtr)data);
                }
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
        /// Does one frame's worth of streaming: points textures at the resolution they need, starts the
        /// loads it can, and gives the GPU whatever mip data has arrived since last time. Call once per
        /// frame, after <see cref="BeginDemandPass"/>, on a thread with a GL context.
        /// </summary>
        /// <param name="frameTime">Length of the previous frame in seconds. This call gets 20% of it, minus
        /// what the previous call spent, so its own cost cannot grow its budget.</param>
        public void Timeslice(float frameTime = 1f / 60f)
        {
            var start = Stopwatch.GetTimestamp();
            var budgetSeconds = Math.Max(0f, frameTime * 0.2f - (float)lastSliceDuration / Stopwatch.Frequency);

            UpdateTargets(throttled: true);
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

            foreach (var stream in loadCandidates)
            {
                var throttleReached = throttled && (requests >= MaxRequestsPerSlice || requestBytes >= MaxRequestBytesPerSlice);

                if (throttleReached || Interlocked.Read(ref bitsInFlight) >= MaxBitsInFlight)
                {
                    pendingLoadRequests.Enqueue(stream);
                    continue;
                }

                var budget = throttled ? (int)(MaxRequestBytesPerSlice - requestBytes) : int.MaxValue;
                var issuedBytes = IssueLoadRequest(stream, budget);

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
            var hookedUp = false;

            SampleVideoMemory();
            IssueQueuedLoads(throttled);

            // Otherwise captures carry a permanent zero-cost debug group
            if (!loadedMipBits.IsEmpty)
            {
                using var _ = new GLDebugGroup("Texture Mip Uploads");

                while (loadedMipBits.TryDequeue(out var bits))
                {
                    var stream = bits.Stream;

                    // Texture deleted while the load was in flight; the chain ends here
                    if (stream.Texture.Handle == 0)
                    {
                        DiscardBits(in bits);
                        continue;
                    }

                    ReleaseInFlight(stream, bits.ByteSize);
                    HookUpMipBits(in bits);
                    hookedUp = true;

                    // The GL upload copies out of the buffer before returning, so it is dead here
                    ArrayPool<byte>.Shared.Return(bits.Buffer);

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
            }

            return hookedUp;
        }

        private bool HasPendingWork
            => !pendingLoadRequests.IsEmpty || !loadedMipBits.IsEmpty || Interlocked.Read(ref bitsInFlight) > 0;

        private void SampleVideoMemory()
        {
            if (HasPendingWork)
            {
                videoMemoryHeadroom = GLEnvironment.GetAvailableVideoMemory() - Interlocked.Read(ref bitsInFlight);
            }
        }

        /// <summary>
        /// Blocks until every texture holds what it needs, to no budget. Must be called on a thread with a
        /// GL context, after <see cref="BeginDemandPass"/>.
        /// </summary>
        /// <param name="cancellationToken">Stops early, leaving chains unfinished.</param>
        public void FinishAllStreaming(CancellationToken cancellationToken = default)
        {
            var backoff = new SpinWait();

            UpdateTargets(throttled: false);

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

        // Returns the bytes requested. Only ever runs on the thread doing the slice, so reading the in-flight
        // byte count cannot race the adds made right below it.
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

            // The chain stays where it got to. The next slice asks again, so it resumes once memory frees up.
            if (videoMemoryHeadroom - stream.GateBytes < VideoMemoryReserve)
            {
                if (!loggedLowVideoMemory)
                {
                    loggedLowVideoMemory = true;
                    rendererContext.Logger.LogWarning("Texture streaming low memory available ({Free} MB)", videoMemoryHeadroom / (1024 * 1024));
                }

                return 0;
            }

            videoMemoryHeadroom -= stream.GateBytes;

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

                HookUpMipBits(new LoadedMipBits(stream, mip, MipCount: 1, mip.BufferSize, buffer));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // The buffer holds exactly the mips: an in-place decode would need a margin past each one, pushing
        // power of two mips into the pool bucket twice their size.
        internal void LoadStreamingData(StreamedTexture stream)
        {
            var first = stream.NextMip;
            var count = stream.RequestMipCount;
            var gateBytes = stream.GateBytes;

            if (stream.Texture.Handle == 0 || suspended)
            {
                ReleaseInFlight(stream, gateBytes);
                return;
            }

            // Owned here until the enqueue hands it over; any exit before that returns it
            byte[]? buffer = null;

            try
            {
                buffer = ArrayPool<byte>.Shared.Rent(gateBytes);

                var offset = 0;

                // The srgb and non-srgb variants of one resource share the Texture block and its reader
                lock (stream.Data)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var mip = stream.Mips[first + i];

                        stream.Data.ReadTextureMipLevel(buffer.AsSpan(offset, mip.BufferSize), stream.DataMipLevel(mip));
                        offset += mip.BufferSize;
                    }
                }

                // Suspended during the read: nothing will upload it, and the finally returns the buffer
                if (suspended)
                {
                    ReleaseInFlight(stream, gateBytes);
                    return;
                }

                loadedMipBits.Enqueue(new LoadedMipBits(stream, stream.Mips[first], count, gateBytes, buffer));
                buffer = null; // handed over
            }
            catch (Exception e) // A corrupt or truncated vtex, or the package disposed under a load still in flight at teardown.
            {
                rendererContext.Logger.LogError(e, "Loading mips of {Texture} failed", stream.Name);
                stream.Failed = true;
                ReleaseInFlight(stream, gateBytes);
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
