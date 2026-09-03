using System.Threading;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>
    /// Bookkeeping for one asynchronously loading texture, doubling as the thread pool work item for
    /// its mip reads. Chain level 0 is the biggest allocated mip of the (possibly user-capped) chain.
    /// Mips load strictly smallest to largest with one read in flight per texture: the next read is
    /// dispatched only once the previous mip's upload has been applied. Reads may one day run a couple
    /// of mips ahead, but must stay sequential — the reveal assumes uploads arrive in chain order.
    /// </summary>
    sealed class StreamedTexture : IThreadPoolWorkItem
    {
        public required TextureStreamingHelper Streaming { get; init; }
        public required string Name { get; init; }
        public required RenderTexture Texture { get; init; }
        public required Texture Data { get; init; }
        public required ImageFormat Format { get; init; }
        public required SizedInternalFormat SizedInternalFormat { get; init; }
        public required bool Is3D { get; init; }
        public required int MinMipLevelAllowed { get; init; }

        /// <summary>Number of levels in the full (possibly user-capped) chain.</summary>
        public int ChainLevels => Mips.Length + 1;

        /// <summary>Dimensions of chain level 0, after the user's size cap. Depth carries the layer
        /// (times face) count for array and cube targets, and the spatial depth for volumes.</summary>
        public required int AllocWidth { get; init; }
        public required int AllocHeight { get; init; }
        public required int AllocDepth { get; init; }

        /// <summary>Chain level held by level 0 of the current storage: everything from here down is
        /// resident. Starts at the smallest level and walks toward 0 as growth recreations land.</summary>
        public int ResidentChainLevel;

        /// <summary>The mips left to load, ordered smallest to largest. The chain's smallest mip uploads
        /// synchronously at load so the texture is never sampleable while empty, and is not planned here.</summary>
        public required PlannedMip[] Mips { get; init; }

        /// <summary>Index into <see cref="Mips"/> of the mip to read next. Advanced by the pump thread
        /// as uploads are applied, read back by the worker the pump then dispatches.</summary>
        public int NextMip;

        /// <summary>How many mips the issued request reads.</summary>
        public int RequestMipCount;
        public int GateBytes;

        /// <summary>Set once the first load request has been issued. A never-started chain can safely be
        /// completed inline by a caller that needs the texture whole.</summary>
        public bool Started;

        /// <summary>The mip's level in the source texture data, past the user's size cap.</summary>
        public uint DataMipLevel(in PlannedMip mip) => (uint)(mip.ChainLevel + MinMipLevelAllowed);

        /// <summary>Buffer size for reading the mip in place.</summary>
        public int InPlaceSize(in PlannedMip mip) => Data.CalculateInPlaceDecompressionBufferSize(DataMipLevel(mip));

        public void Execute() => Streaming.LoadStreamingData(this);
    }

    /// <summary>One mip level of a <see cref="StreamedTexture"/> waiting to be read.</summary>
    readonly record struct PlannedMip(int ChainLevel, int Width, int Height, int Depth, int BufferSize);

    /// <summary>Loaded mip bits waiting for <see cref="TextureStreamingHelper"/> to hook them up on the
    /// render thread: one mip, or a texture's whole remaining chain packed into one buffer.</summary>
    /// <param name="Stream">The texture the data belongs to.</param>
    /// <param name="Mip">The first (smallest) mip in the buffer.</param>
    /// <param name="MipCount">Number of consecutive planned mips the buffer holds, starting at <paramref name="Mip"/>.</param>
    /// <param name="ByteSize">The amount this item holds of the in-flight throttle, unwound when it is consumed.</param>
    /// <param name="Buffer">Pooled buffer with each mip at its in-place read offset, in plan order.</param>
    readonly record struct LoadedMipBits(StreamedTexture Stream, PlannedMip Mip, int MipCount, int ByteSize, byte[] Buffer);
}
