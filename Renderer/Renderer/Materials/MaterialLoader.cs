using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO.Hashing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;
using VrfMaterial = ValveResourceFormat.ResourceTypes.Material;

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
        public required MaterialLoader Loader { get; init; }
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

        /// <summary>Chain level held by level 0 of the current storage. Starts at the smallest level
        /// and walks toward 0 as growth recreations land.</summary>
        public int TopChainLevel;

        /// <summary>The mips left to load, ordered smallest to largest. The chain's smallest mip uploads
        /// synchronously at load so the texture is never sampleable while empty, and is not planned here.</summary>
        public required PlannedMip[] Mips { get; init; }

        /// <summary>Index into <see cref="Mips"/> of the mip to read next. Advanced by the pump thread
        /// as uploads are applied, read back by the worker the pump then dispatches.</summary>
        public int NextMip;

        /// <summary>Set once the first read has been dispatched. A never-started chain can safely be
        /// completed inline by a caller that needs the texture whole.</summary>
        public bool Started;

        /// <summary>The mip's level in the source texture data, past the user's size cap.</summary>
        public uint DataMipLevel(in PlannedMip mip) => (uint)(mip.ChainLevel + MinMipLevelAllowed);

        /// <summary>Buffer size for reading the mip, including the in-place LZ4 decompression margin.</summary>
        public int InPlaceSize(in PlannedMip mip) => Data.CalculateInPlaceDecompressionBufferSize(DataMipLevel(mip));

        public void Execute() => Loader.ExecuteRead(this);
    }

    /// <summary>One mip level of a <see cref="StreamedTexture"/> waiting to be read.</summary>
    readonly record struct PlannedMip(int ChainLevel, int Width, int Height, int Depth, int BufferSize);

    /// <summary>Read mip data waiting for <see cref="MaterialLoader"/> to apply it on the render thread:
    /// one mip, or in batched mode a texture's whole remaining chain packed into one buffer.</summary>
    /// <param name="Stream">The texture the data belongs to.</param>
    /// <param name="Mip">The first (smallest) mip in the buffer.</param>
    /// <param name="MipCount">Number of consecutive planned mips the buffer holds, starting at <paramref name="Mip"/>.</param>
    /// <param name="ByteSize">The amount this item holds of the in-flight byte gate, unwound when it is consumed.</param>
    /// <param name="Buffer">Pooled buffer with each mip at its in-place read offset, in plan order.</param>
    readonly record struct MipUploadData(StreamedTexture Stream, PlannedMip Mip, int MipCount, int ByteSize, byte[] Buffer);

    /// <summary>
    /// Loads and caches materials and textures from Source 2 resources.
    /// </summary>
    public class MaterialLoader
    {
        private readonly Dictionary<ulong, RenderMaterial> Materials = [];
        private readonly List<RenderMaterial> OwnedMaterials = [];

        private readonly Dictionary<string, RenderTexture> Textures = [];
        private readonly Dictionary<string, RenderTexture> TexturesSrgb = [];
        private readonly Dictionary<(RsTextureAddressMode AddressU, RsTextureAddressMode AddressV, bool Mipmaps, bool AnisotropicFiltering), int> Samplers = [];
        private readonly RendererContext RendererContext;
        private RenderTexture? ErrorTexture;
        private RenderTexture? DefaultNormal;
        private RenderTexture? DefaultMask;
        private RenderTexture? DefaultColor;
        private RenderTexture? DefaultVolume;
        /// <summary>Gets or sets the maximum anisotropy level applied to newly loaded textures when anisotropic filtering is enabled.</summary>
        public static float MaxTextureMaxAnisotropy { get; set; }

        /// <summary>Gets the number of materials currently held in the cache.</summary>
        public int MaterialCount => Materials.Count;

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
        public bool BatchChainReads { get; set; }

        /// <summary>Incomplete streams by their texture, so a non-streaming request for a texture that a
        /// material already started streaming can finish the chain inline instead of sampling a stub.</summary>
        private readonly ConcurrentDictionary<RenderTexture, StreamedTexture> incompleteStreams = new();

        /// <summary>Retires a chain at any of its terminal points: completed, failed, or dropped with its
        /// texture. Idempotent — a retired chain can still be dequeued from the pending queue by a later drain.</summary>
        private void RetireStream(StreamedTexture stream)
        {
            incompleteStreams.TryRemove(stream.Texture, out _);
        }

        /// <summary>
        /// Maps a material texture parameter name to the shader uniforms it can feed, in preference order.
        /// The first candidate the shader declares and that is not already bound wins.
        /// </summary>
        private static readonly Dictionary<string, string[]> TextureAliases = new(StringComparer.Ordinal)
        {
            ["g_tColor1"] = ["g_tColor"],
            ["g_tColor2"] = ["g_tColor", "g_tLayer2Color"],
            ["g_tColorA"] = ["g_tColor"],
            ["g_tColorB"] = ["g_tLayer2Color", "g_tColor"],
            ["g_tColorC"] = ["g_tColor"],
            ["g_tGlassDust"] = ["g_tColor"],
            ["g_tNormalA"] = ["g_tNormal"],
            ["g_tNormalB"] = ["g_tLayer2NormalRoughness"],
            ["g_tNormalRoughness"] = ["g_tNormal"],
            ["g_tNormalRoughness1"] = ["g_tNormal"],
            ["g_tNormalRoughness2"] = ["g_tLayer2NormalRoughness"],
            ["g_tLayer1NormalRoughness"] = ["g_tNormal"],
            ["g_tLayer1AmbientOcclusion"] = ["g_tAmbientOcclusion"],
        };

        /// <summary>Initializes a new instance of the <see cref="MaterialLoader"/> class.</summary>
        /// <param name="rendererContext">The renderer context used for file loading and shader access.</param>
        public MaterialLoader(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
        }

        private static readonly byte[] NewLineArray = "\n"u8.ToArray();

        /// <summary>
        /// Clears the material cache and disposes any cached textures and samplers.
        /// </summary>
        public void Clear()
        {
            foreach (var material in OwnedMaterials)
            {
                material.Delete();
            }

            OwnedMaterials.Clear();
            Materials.Clear();

            foreach (var item in Textures)
            {
                item.Value.Delete();
            }

            Textures.Clear();

            foreach (var item in TexturesSrgb)
            {
                item.Value.Delete();
            }

            TexturesSrgb.Clear();

            foreach (var sampler in Samplers.Values)
            {
                GL.DeleteSampler(sampler);
            }

            Samplers.Clear();

            DrainPendingStreams();
        }

        /// <summary>Drops all pending stream work. Makes no GL calls, so it is also safe during teardown
        /// without a current context. The byte gate is unwound per item rather than zeroed: a straggling
        /// read's bytes stay counted until its upload surfaces, so the counter never goes negative.</summary>
        public void DrainPendingStreams()
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

        /// <summary>Returns a cached <see cref="RenderMaterial"/> for the given resource path and shader arguments, loading and caching it on first access.</summary>
        /// <param name="name">The compiled material resource path, or <see langword="null"/> to return the error material.</param>
        /// <param name="shaderArguments">Optional static combo overrides to pass to the shader.</param>
        public RenderMaterial GetMaterial(string? name, Dictionary<string, byte>? shaderArguments)
        {
            // HL:VR has a world node that has a draw call with no material
            if (name == null)
            {
                return GetErrorMaterial();
            }

            Span<byte> valueSpan = stackalloc byte[1];
            var hash = new XxHash3(StringToken.MURMUR2SEED);
            hash.Append(MemoryMarshal.AsBytes(name.AsSpan()));

            if (shaderArguments != null)
            {
                foreach (var (key, value) in shaderArguments)
                {
                    hash.Append(NewLineArray);
                    hash.Append(MemoryMarshal.AsBytes(key.AsSpan()));
                    hash.Append(NewLineArray);

                    valueSpan[0] = value;
                    hash.Append(valueSpan);
                }
            }

            var cacheKey = hash.GetCurrentHashAsUInt64();

            if (Materials.TryGetValue(cacheKey, out var mat))
            {
                return mat;
            }

            var resource = RendererContext.FileLoader.LoadFileCompiled(name);
            mat = LoadMaterial(resource, shaderArguments);

            Materials.Add(cacheKey, mat);

            return mat;
        }

        /// <summary>Creates a <see cref="RenderMaterial"/> from an already-loaded resource, binding textures and resolving aliases.</summary>
        /// <param name="resource">The material resource, or <see langword="null"/> to return the error material.</param>
        /// <param name="shaderArguments">Optional static combo overrides to pass to the shader.</param>
        public RenderMaterial LoadMaterial(Resource? resource, Dictionary<string, byte>? shaderArguments = null)
        {
            if (resource == null)
            {
                return GetErrorMaterial();
            }

            var vrfMaterial = (VrfMaterial?)resource.DataBlock;
            Debug.Assert(vrfMaterial != null);
            var mat = new RenderMaterial(
                vrfMaterial,
                RendererContext,
                shaderArguments
            );

            OwnedMaterials.Add(mat);

            foreach (var (textureName, texturePath) in mat.Material.TextureParams)
            {
                TryBindTexture(mat, textureName, texturePath);
            }

            foreach (var (textureName, texturePath) in mat.Material.TextureParams)
            {
                if (mat.Textures.ContainsKey(textureName)
                || !TextureAliases.TryGetValue(textureName, out var aliases))
                {
                    continue;
                }

                foreach (var alias in aliases)
                {
                    if (mat.Textures.ContainsKey(alias))
                    {
                        continue;
                    }

                    if (TryBindTexture(mat, alias, texturePath))
                    {
                        break;
                    }
                }
            }

            bool TryBindTexture(RenderMaterial mat, string name, string path)
            {
                if (mat.Shader.UniformNames.Contains(name))
                {
                    var srgbRead = mat.Shader.SrgbUniforms.Contains(name);
                    mat.Textures[name] = GetTexture(path, srgbRead, anisotropicFiltering: true, streaming: true);
                    return true;
                }

                return false;
            }

            return mat;
        }

        /// <summary>Returns a cached <see cref="RenderTexture"/> for the given path, loading it on first access.</summary>
        /// <param name="name">The compiled texture resource path.</param>
        /// <param name="srgbRead">Whether to interpret the texture data in sRGB color space.</param>
        /// <param name="anisotropicFiltering">Whether to apply anisotropic filtering when <see cref="MaxTextureMaxAnisotropy"/> is sufficient.</param>
        /// <param name="streaming">Whether the texture may load progressively over the frames after this
        /// call. Off by default: only textures that are exclusively sampled by draws — material and
        /// particle textures — should opt in. Anything read or copied at load time (lightmaps, envmaps,
        /// light probes, atlas sources) or snapshotted into constants must stay synchronous and complete.</param>
        public RenderTexture GetTexture(string name, bool srgbRead = false, bool anisotropicFiltering = false, bool streaming = false)
        {
            // TODO: Create texture view for srgb textures
            var cache = srgbRead ? TexturesSrgb : Textures;

            if (cache.TryGetValue(name, out var tex))
            {
                // A non-streaming caller needs the texture complete, even when a material started it streaming
                if (!streaming)
                {
                    FinishStreaming(tex);
                }

                return tex;
            }

            tex = LoadTexture(name, srgbRead, async: streaming);
            cache.Add(name, tex);

            if (anisotropicFiltering && MaxTextureMaxAnisotropy >= 4)
            {
                // Through the texture, so growth recreations reapply it to each replacement object
                tex.SetMaxAnisotropy(MaxTextureMaxAnisotropy);
            }

            return tex;
        }

        /// <summary>
        /// Gets a sampler object for the supplied texture address modes, creating and caching one per <see cref="MaterialLoader" />.
        /// </summary>
        public int GetOrCreateSampler(RsTextureAddressMode addressModeU, RsTextureAddressMode addressModeV, bool mipmaps = true, bool anisotropicFiltering = true)
        {
            var key = (addressModeU, addressModeV, mipmaps, anisotropicFiltering);

            if (key == (RsTextureAddressMode.Wrap, RsTextureAddressMode.Wrap, true, true))
            {
                return 0; // the default sampler state already wraps
            }

            if (Samplers.TryGetValue(key, out var sampler))
            {
                return sampler;
            }

            var newSampler = new Sampler($"Sampler{addressModeU}{addressModeV}");

            newSampler.SetWrapMode(addressModeU, addressModeV);
            newSampler.SetFiltering(mipmaps ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear, TextureMagFilter.Linear);

            if (anisotropicFiltering && MaxTextureMaxAnisotropy >= 4)
            {
                newSampler.SetMaxAnisotropy(MaxTextureMaxAnisotropy);
            }

            Samplers[key] = newSampler.Handle;
            return newSampler.Handle;
        }

        private RenderTexture LoadTexture(string name, bool srgbRead = false, bool async = false)
        {
            var textureResource = RendererContext.FileLoader.LoadFileCompiled(name);

            if (textureResource == null)
            {
                return GetErrorTexture();
            }

            return LoadTexture(textureResource, srgbRead, isViewerRequest: false, async);
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

            // Start newly loaded textures and restart chains parked by the byte gate
            while (Interlocked.Read(ref pendingUploadBytes) < MaxPendingUploadBytes && pendingStreams.TryDequeue(out var stream))
            {
                StartNextRead(stream);
            }

            var budgetSeconds = Math.Max(0f, frameTime * 0.2f - (float)lastUploadDuration / Stopwatch.Frequency);
            var deadline = start + (long)(budgetSeconds * Stopwatch.Frequency);

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

            lastUploadDuration = Stopwatch.GetTimestamp() - start;
        }

        /// <summary>How long the previous <see cref="UploadPendingTextures"/> took, in <see cref="Stopwatch"/> ticks.</summary>
        private long lastUploadDuration;

        /// <summary>Whether any streamed texture still has mips waiting to start, in flight on a read job,
        /// or awaiting upload. While true, <see cref="UploadPendingTextures"/> still has work to do.</summary>
        private bool HasPendingTextureStreams
            => !pendingStreams.IsEmpty || !pendingMipUploads.IsEmpty || Interlocked.Read(ref pendingUploadBytes) > 0;

        /// <summary>Pumps until every stream has finished, for one-shot consumers that have no frame
        /// loop to pump later. Must be called on a thread with a GL context.</summary>
        public void FinishAllStreaming(CancellationToken cancellationToken = default)
        {
            while (HasPendingTextureStreams && !cancellationToken.IsCancellationRequested)
            {
                UploadPendingTextures(frameTime: 1f);
                Thread.Yield(); // reads may still be in flight on the thread pool with nothing to apply yet
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

        /// <summary>Data bytes the stream's next read holds of the byte gate: one mip, or in batched mode
        /// the whole remaining chain. Also the read's unwind amount on failure, so both compute it here.</summary>
        private int PendingReadBytes(StreamedTexture stream)
        {
            if (!BatchChainReads)
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

        /// <summary>Reads a stream's next mip — or in batched mode its whole remaining chain — into one
        /// pooled buffer, each mip decoded in place at its own offset, and queues it for the pump.</summary>
        internal void ExecuteRead(StreamedTexture stream)
        {
            var first = stream.NextMip;
            var count = BatchChainReads ? stream.Mips.Length - first : 1;
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

                // Returned to the pool by the upload pump once the data is handed to the driver. Sized
                // for in-place LZ4 decompression, so the reads need no second scratch buffer.
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

#pragma warning disable CA1822 // Mark members as static
        /// <summary>Uploads a texture resource to the GPU and returns the resulting <see cref="RenderTexture"/>.</summary>
        /// <param name="textureResource">The loaded texture resource.</param>
        /// <param name="srgbRead">Whether to use the sRGB internal format when available.</param>
        /// <param name="isViewerRequest">When <see langword="true"/>, skips mip-level capping.</param>
        /// <param name="async">When <see langword="true"/>, mip data is read on background jobs and uploaded later by <see cref="UploadPendingTextures"/>, smallest mips first.</param>
        public RenderTexture LoadTexture(Resource textureResource, bool srgbRead = false, bool isViewerRequest = false, bool async = false)
#pragma warning restore CA1822 // Mark members as static
        {
            var data = (Texture?)textureResource.DataBlock;

            if (data == null)
            {
                RendererContext.Logger.LogError("Texture resource {FileName} has no data block, using error texture", textureResource.FileName);
                Debug.Assert(false, $"{textureResource.FileName} has no data block");
                return GetErrorTexture();
            }

            if (data.IsRawAnyImage)
            {
                using var bitmap = data.GenerateBitmap();
                return LoadBitmapTexture(bitmap);
            }

            var target = TextureTarget.Texture2D;
            var is3d = false;
            var clampModeS = (data.Flags & VTexFlags.SUGGEST_CLAMPS) != 0 ? RsTextureAddressMode.Border : RsTextureAddressMode.Wrap;
            var clampModeT = (data.Flags & VTexFlags.SUGGEST_CLAMPT) != 0 ? RsTextureAddressMode.Border : RsTextureAddressMode.Wrap;
            var clampModeU = (data.Flags & VTexFlags.SUGGEST_CLAMPU) != 0 ? RsTextureAddressMode.Border : RsTextureAddressMode.Wrap;

            if ((data.Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                is3d = true;
                target = (data.Flags & VTexFlags.TEXTURE_ARRAY) != 0 ? TextureTarget.TextureCubeMapArray : TextureTarget.TextureCubeMap;
                clampModeS = RsTextureAddressMode.Clamp;
                clampModeT = RsTextureAddressMode.Clamp;
                clampModeU = RsTextureAddressMode.Clamp;
            }
            else if ((data.Flags & (VTexFlags.TEXTURE_ARRAY | VTexFlags.VOLUME_TEXTURE)) != 0)
            {
                is3d = true;
                target = (data.Flags & VTexFlags.VOLUME_TEXTURE) != 0 ? TextureTarget.Texture3D : TextureTarget.Texture2DArray;
            }

            var textureName = System.IO.Path.GetFileName(textureResource.FileName) ?? "UnnamedTexture";
            var tex = new RenderTexture(target, data, textureName);
            var format = GetTextureFormat(data.Format);
            var srgb = srgbRead && format.HasSrgbVariant();

            // todo: BC7 and BC6H are also problematic on pre-RDNA AMD GPUs, when using immutable storage
            // see https://github.com/ValveResourceFormat/ValveResourceFormat/issues/721
            var rgba8UncompressedFallback = target == TextureTarget.Texture3D && IsOpenGLUnsupportedTexture3DFormat(data.Format);

            if (rgba8UncompressedFallback)
            {
                format = ImageFormat.RGBA8888;
            }

            var sizedInternalFormat = format.ToGLSizedInternalFormat(srgb);

            var texDepth = data.Depth;

            if (target == TextureTarget.TextureCubeMap || target == TextureTarget.TextureCubeMapArray)
            {
                texDepth *= 6;
            }

            var minMipLevelAllowed = 0;
            var texWidth = data.Width;
            var texHeight = data.Height;

            if (!isViewerRequest && !is3d && data.NumMipLevels > 1)
            {
                var maxUserTextureSize = RendererContext.MaxTextureSize;

                while (minMipLevelAllowed + 1 < data.NumMipLevels && (texWidth > maxUserTextureSize || texHeight > maxUserTextureSize))
                {
                    minMipLevelAllowed++;

                    texWidth >>= 1;
                    texHeight >>= 1;
                }
            }

            var chainLevels = data.NumMipLevels - minMipLevelAllowed;

            // The decode fallback needs the whole chain read up front, so it stays on the synchronous
            // path — as does a single-mip texture, which has nothing left to stream after its first upload
            var streamable = async && !rgba8UncompressedFallback && chainLevels > 1;

            // Streamed textures are born holding only their smallest mip and grow as data arrives,
            // so VRAM is committed by the upload pump instead of all at once during the load phase
            if (streamable)
            {
                var (smallestWidth, smallestHeight, smallestDepth) = GetChainLevelSize(target, texWidth, texHeight, texDepth, chainLevels - 1);

                CreateStorageForTarget(tex.Handle, target, levels: 1, sizedInternalFormat, smallestWidth, smallestHeight, smallestDepth);
            }
            else
            {
                CreateStorageForTarget(tex.Handle, target, chainLevels, sizedInternalFormat, texWidth, texHeight, texDepth);
            }

            tex.SetFiltering(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
            tex.SetWrapMode(clampModeS, clampModeT, clampModeU);

            if (streamable)
            {
                var stream = new StreamedTexture
                {
                    Loader = this,
                    Name = textureName,
                    Texture = tex,
                    Data = data,
                    Format = format,
                    SizedInternalFormat = sizedInternalFormat,
                    Is3D = is3d,
                    MinMipLevelAllowed = minMipLevelAllowed,
                    AllocWidth = texWidth,
                    AllocHeight = texHeight,
                    AllocDepth = texDepth,
                    TopChainLevel = chainLevels - 1,
                    Mips = new PlannedMip[chainLevels - 1],
                };

                var planned = 0;

                foreach (var mipData in data.GetEveryMipLevelMetrics())
                {
                    if (mipData.Level < minMipLevelAllowed)
                    {
                        continue;
                    }

                    var chainLevel = (int)mipData.Level - minMipLevelAllowed;
                    var mip = new PlannedMip(chainLevel, mipData.Width, mipData.Height, mipData.Depth, mipData.BufferSize);

                    if (chainLevel == chainLevels - 1)
                    {
                        // The smallest mip loads synchronously, so the texture is complete and safely
                        // sampleable from its very first draw
                        ReadAndApplyMip(stream, mip);
                        continue;
                    }

                    // The metrics enumerate smallest first, which is the order the chain reads in
                    stream.Mips[planned++] = mip;
                }

                Debug.Assert(planned == stream.Mips.Length);

                incompleteStreams.TryAdd(tex, stream);
                pendingStreams.Enqueue(stream);

                return tex;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(data.GetBiggestBufferSize());
            byte[]? decodedBuffer = null;

            if (rgba8UncompressedFallback)
            {
                decodedBuffer = ArrayPool<byte>.Shared.Rent(data.Width * data.Height * data.Depth * 4);
            }

            try
            {
                // Under the data lock: the other srgb variant of this resource may be streaming, and its
                // read jobs share the block's reader with this loop
                lock (data)
                {
                    foreach (var (level, width, height, depth, bufferSize) in data.GetEveryMipLevelTexture(buffer, minMipLevelAllowed))
                    {
                        var realLevel = (int)level - minMipLevelAllowed;
                        var uploadBuffer = buffer;

                        if (decodedBuffer != null)
                        {
                            data.DecodeTexture(buffer.AsSpan(0, bufferSize), decodedBuffer, width, height, depth);
                            uploadBuffer = decodedBuffer;
                        }

                        if (!format.IsBlockCompressed())
                        {
                            if (is3d)
                            {
                                GL.TextureSubImage3D(tex.Handle, realLevel, 0, 0, 0, width, height, depth, format.ToGLPixelFormat(), format.ToGLPixelType(), uploadBuffer);
                            }
                            else
                            {
                                GL.TextureSubImage2D(tex.Handle, realLevel, 0, 0, width, height, format.ToGLPixelFormat(), format.ToGLPixelType(), uploadBuffer);
                            }
                        }
                        else
                        {
                            if (is3d)
                            {
                                GL.CompressedTextureSubImage3D(tex.Handle, realLevel, 0, 0, 0, width, height, depth, (PixelFormat)sizedInternalFormat, bufferSize, uploadBuffer);
                            }
                            else
                            {
                                GL.CompressedTextureSubImage2D(tex.Handle, realLevel, 0, 0, width, height, (PixelFormat)sizedInternalFormat, bufferSize, uploadBuffer);
                            }
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);

                if (decodedBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(decodedBuffer);
                }
            }

            // The resource is not disposed: it may be a cached instance handed out again, or shared
            // with a streaming variant still reading it, and its reader is cheap to leave to the GC

            return tex;
        }

        /// <summary>
        /// Whether a format has to be decompressed before it can be uploaded to a <see cref="TextureTarget.Texture3D"/>.
        /// Of the block compressed formats only BPTC is specified to work with 3D textures, as a stack of
        /// independently compressed 2D slices. S3TC and RGTC are two-dimensional only:
        /// NVIDIA accepts them through NV_texture_compression_vtc, which reuses the very same format enums but expects
        /// 4x4x4 VTC tiling, so the slices get read back scrambled, and other drivers reject the upload outright.
        /// </summary>
        private static bool IsOpenGLUnsupportedTexture3DFormat(VTexFormat vformat) => vformat
            is VTexFormat.DXT1
            or VTexFormat.DXT5
            or VTexFormat.ATI1N
            or VTexFormat.ATI2N;

        private static ImageFormat GetTextureFormat(VTexFormat vformat) => vformat switch
        {
#pragma warning disable format
            VTexFormat.ATI1N           => ImageFormat.ATI1N,
            VTexFormat.ATI2N           => ImageFormat.ATI2N,
            VTexFormat.BC6H            => ImageFormat.BC6H,
            VTexFormat.BC7             => ImageFormat.BC7,
            VTexFormat.DXT1            => ImageFormat.DXT1,
            VTexFormat.DXT5            => ImageFormat.DXT5,
            VTexFormat.ETC2            => ImageFormat.R8G8B8_ETC2,
            VTexFormat.ETC2_EAC        => ImageFormat.R8G8B8A8_ETC2_EAC,

            VTexFormat.R16             => ImageFormat.R16,
            VTexFormat.RG1616          => ImageFormat.RG1616,
            VTexFormat.RGBA16161616    => ImageFormat.RGBA16161616,

            VTexFormat.R16F            => ImageFormat.R16F,
            VTexFormat.RG1616F         => ImageFormat.RG1616F,
            VTexFormat.RGBA16161616F   => ImageFormat.RGBA16161616F,

            VTexFormat.R32F            => ImageFormat.R32F,
            VTexFormat.RG3232F         => ImageFormat.RG3232F,
            VTexFormat.RGBA32323232F   => ImageFormat.RGBA32323232F,

            VTexFormat.RGBA8888        => ImageFormat.RGBA8888,
            VTexFormat.BGRA8888        => ImageFormat.BGRA8888,
            VTexFormat.I8              => ImageFormat.I8,

            //VTexFormat.IA88
            //VTexFormat.R11_EAC
            //VTexFormat.RG11_EAC
            //VTexFormat.RGB323232F
#pragma warning restore format

            _ => throw new NotImplementedException($"Unsupported texture format {vformat}")
        };

        /// <summary>Gets the texture unit each reserved sampler uniform is bound to.</summary>
        public static readonly FrozenDictionary<string, ReservedTextureSlots> ReservedTextureSlotByName = BuildReservedTextureSlotByName();

        private static FrozenDictionary<string, ReservedTextureSlots> BuildReservedTextureSlotByName()
        {
            var slotByName = new Dictionary<string, ReservedTextureSlots>(StringComparer.Ordinal);

            foreach (var field in typeof(ReservedTextureSlots).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = field.GetCustomAttribute<SamplerNameAttribute>();

                if (attribute == null)
                {
                    continue; // Aliases such as Last carry no names of their own.
                }

                var slot = (ReservedTextureSlots)field.GetRawConstantValue()!;

                foreach (var name in attribute.Names)
                {
                    // Add, not assign: two slots claiming one sampler name is a mistake worth failing on.
                    slotByName.Add(name, slot);
                }
            }

            return slotByName.ToFrozenDictionary(StringComparer.Ordinal);
        }

        /// <summary>Returns whether a uniform name is bound to one of the <see cref="ReservedTextureSlots"/>.</summary>
        public static bool IsReservedTexture(string uniformName) => ReservedTextureSlotByName.ContainsKey(uniformName);

        /// <summary>
        /// Material invariant textures, requested by shaders. They become scene-wide textures.
        /// </summary>
        public static readonly List<(ReservedTextureSlots Slot, string Name, string Path)> ShaderTextures =
        [
            (ReservedTextureSlots.WetnessWaves, "g_tWetnessWaves", "materials/dev/water_waves.vtex"),
        ];

        private RenderMaterial GetErrorMaterial()
        {
            var errorMat = new RenderMaterial(RendererContext.ShaderLoader.LoadShader("error"));
            OwnedMaterials.Add(errorMat);
            return errorMat;
        }

        /// <summary>Returns a lazily created 4×4 checkerboard error texture used as a fallback for missing textures.</summary>
        public RenderTexture GetErrorTexture()
        {
            if (ErrorTexture == null)
            {
                ReadOnlySpan<byte> color1 = [100, 25, 75];
                ReadOnlySpan<byte> color2 = [0, 127, 0];

                var color = new byte[16 * 3];

                for (var i = 0; i < 16; i++)
                {
                    var checkerboardX = i / 4 % 2;
                    var colorToUse = i % 2 == checkerboardX ? color1 : color2;
                    var pixel = color.AsSpan(i * 3, 3);
                    colorToUse.CopyTo(pixel);
                }

                ErrorTexture = GenerateColorTexture(4, 4, color);
            }

            return ErrorTexture;
        }

        private static RenderTexture CreateSolidTexture(byte r, byte g, byte b) => GenerateColorTexture(1, 1, [r, g, b]);
        /// <summary>Returns a lazily created 1×1 flat normal map texture (127, 127, 255).</summary>
        public RenderTexture GetDefaultNormal() => DefaultNormal ??= CreateSolidTexture(127, 127, 255);

        /// <summary>Returns a lazily created 1×1 solid white mask texture.</summary>
        public RenderTexture GetDefaultMask() => DefaultMask ??= CreateSolidTexture(255, 255, 255);

        /// <summary>Returns a lazily created 1×1 solid white colour texture, a neutral fallback albedo.</summary>
        public RenderTexture GetDefaultColor() => DefaultColor ??= CreateSolidTexture(255, 255, 255);

        /// <summary>
        /// Returns a lazily created 1×1×1 white volume texture.
        /// </summary>
        public RenderTexture GetDefaultVolume()
        {
            if (DefaultVolume == null)
            {
                DefaultVolume = RenderTexture.Create3D(TextureTarget.Texture3D, 1, 1, 1, ImageFormat.RGBA8888, 1, "DefaultVolume");
                DefaultVolume.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                DefaultVolume.SetWrapMode(RsTextureAddressMode.Clamp);
                GL.TextureSubImage3D(DefaultVolume.Handle, 0, 0, 0, 0, 1, 1, 1, PixelFormat.Rgb, PixelType.UnsignedByte, WhiteTexel);
            }

            return DefaultVolume;
        }

        private static readonly byte[] WhiteTexel = [255, 255, 255];

        /// <summary>Returns the readback format appropriate for exporting a rendered image: 8-bit BGRA, or 32-bit float RGBA for HDR.</summary>
        /// <param name="hdr">Whether to use the HDR (32-bit float) format.</param>
        public static ImageFormat GetImageExportFormat(bool hdr)
            => hdr ? ImageFormat.RGBA32323232F : ImageFormat.BGRA8888;

        /// <summary>Uploads an <see cref="SKBitmap"/> as a 2D texture and returns the resulting <see cref="RenderTexture"/>.</summary>
        /// <param name="bitmap">The bitmap whose pixels are uploaded to the GPU.</param>
        public static RenderTexture LoadBitmapTexture(SKBitmap bitmap)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, bitmap.Width, bitmap.Height, 1, 1, "BitmapTexture");

            var format = bitmap.ColorType switch
            {
                SKColorType.Rgba8888 => ImageFormat.RGBA8888,
                SKColorType.Bgra8888 => ImageFormat.BGRA8888,
                SKColorType.Rgb888x => ImageFormat.RGBA8888,
                SKColorType.Gray8 => ImageFormat.I8,
                SKColorType.RgbaF16 => ImageFormat.RGBA16161616F,
                SKColorType.RgbaF32 => ImageFormat.RGBA32323232F,
                _ => throw new NotSupportedException($"Unsupported bitmap color type for GPU upload {bitmap.ColorType}"),
            };

            GL.TextureStorage2D(texture.Handle, 1, format.ToGLSizedInternalFormat(), texture.Width, texture.Height);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, texture.Width, texture.Height, format.ToGLPixelFormat(), format.ToGLPixelType(), bitmap.GetPixels());

            if (bitmap.ColorType == SKColorType.Rgb888x)
            {
                // DXGI has no RGBX storage; keep alpha reading as one like the old Rgb8 storage did.
                texture.SetParameter(TextureParameterName.TextureSwizzleA, (int)All.One);
            }

            if (bitmap.ColorType == SKColorType.Rgb888x)
            {
                // The uploaded fourth byte is undefined, the format is opaque by definition
                GL.TextureParameter(texture.Handle, TextureParameterName.TextureSwizzleA, (int)All.One);
            }

            return texture;
        }

        /// <summary>
        /// Builds a one-dimensional colour ramp from a list of gradient stops.
        /// </summary>
        /// <param name="stops">Gradient stops, each a position in 0-1 and its colour. Need not be sorted.</param>
        public static RenderTexture GenerateGradientTexture(ReadOnlySpan<(float Position, Color32 Color)> stops)
        {
            const int Width = 256;

            var texels = new byte[Width * 4];

            for (var x = 0; x < Width; x++)
            {
                var position = x / (Width - 1f);
                var color = SampleGradient(stops, position);

                texels[(x * 4) + 0] = color.R;
                texels[(x * 4) + 1] = color.G;
                texels[(x * 4) + 2] = color.B;
                texels[(x * 4) + 3] = color.A;
            }

            var texture = new RenderTexture(TextureTarget.Texture2D, Width, 1, 1, 1, "GeneratedGradient");

            // Clamped and filtered: the ramp is addressed by a luminance, so the ends have to hold rather
            // than wrap, and the steps between stops should not be visible.
            texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            texture.SetWrapMode(RsTextureAddressMode.Clamp);

            // sRGB storage, so a sample lands in linear space like every other layer's texture.
            GL.TextureStorage2D(texture.Handle, 1, SizedInternalFormat.Srgb8Alpha8, Width, 1);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, Width, 1, PixelFormat.Rgba, PixelType.UnsignedByte, texels);

            return texture;
        }

        private static Color32 SampleGradient(ReadOnlySpan<(float Position, Color32 Color)> stops, float position)
        {
            if (stops.Length == 0)
            {
                return new Color32(255, 255, 255);
            }

            // Stops are authored in order, but nothing guarantees it, so pick the bracketing pair by value
            // rather than by index.
            var lower = stops[0];
            var upper = stops[0];
            var hasLower = false;
            var hasUpper = false;

            foreach (var stop in stops)
            {
                if (stop.Position <= position && (!hasLower || stop.Position >= lower.Position))
                {
                    lower = stop;
                    hasLower = true;
                }

                if (stop.Position >= position && (!hasUpper || stop.Position <= upper.Position))
                {
                    upper = stop;
                    hasUpper = true;
                }
            }

            if (!hasLower)
            {
                return upper.Color;
            }

            if (!hasUpper)
            {
                return lower.Color;
            }

            var span = upper.Position - lower.Position;
            var t = span > 0f ? (position - lower.Position) / span : 0f;

            return new Color32(
                (byte)float.Round(float.Lerp(lower.Color.R, upper.Color.R, t)),
                (byte)float.Round(float.Lerp(lower.Color.G, upper.Color.G, t)),
                (byte)float.Round(float.Lerp(lower.Color.B, upper.Color.B, t)),
                (byte)float.Round(float.Lerp(lower.Color.A, upper.Color.A, t)));
        }

        private static RenderTexture GenerateColorTexture(int width, int height, byte[] color)
        {
            // Full mip chain, because materials may bind a mipmap filtering sampler over this
            // texture, and an incomplete mip chain would then sample as if nothing was bound
            var levels = 1 + BitOperations.Log2((uint)Math.Max(width, height));

            var texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, levels, width > 1 ? "ErrorTexture" : "ColorTexture");
            texture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            texture.SetWrapMode(RsTextureAddressMode.Wrap);

            var color32 = new Color32(color[0], color[1], color[2]);
            texture.Reflectivity = color32.ToLinearColor();

            GL.TextureStorage2D(texture.Handle, levels, SizedInternalFormat.Rgba8, width, height);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, color);

            if (levels > 1)
            {
                GL.GenerateTextureMipmap(texture.Handle);
            }

            return texture;
        }
    }
}
