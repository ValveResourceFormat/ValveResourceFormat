using System.Buffers;
using System.Diagnostics;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// One kind of decal texture, such as every decal's colour, packed into an array texture so a single
    /// draw can sample any decal. Layers are sized for the largest texture added, and a smaller texture
    /// fills the corner of its layer, mip for mip, which the sampler reaches through a scale.
    /// </summary>
    internal sealed class ProjectedDecalTextureArray
    {
        /// <summary>A texture's place in the array.</summary>
        public readonly record struct Layer(int Index, int Width, int Height);

        private const int BlockSize = 4;

        private readonly string label;
        private readonly VTexFormat format;
        private readonly ImageFormat imageFormat;
        private readonly bool srgb;
        private readonly byte[] clearBlock;

        private readonly List<string> layerPaths = [];
        private readonly Dictionary<string, Layer> layersByPath = [];

        private int capacity;
        private int levels;

        /// <summary>Gets the array texture, or null before anything was added.</summary>
        public RenderTexture? ArrayTexture { get; private set; }

        /// <summary>Gets the layer width, the widest texture added.</summary>
        public int Width { get; private set; }

        /// <summary>Gets the layer height, the tallest texture added.</summary>
        public int Height { get; private set; }

        /// <param name="label">Label visible in graphics debuggers.</param>
        /// <param name="format">The only texture format this array accepts.</param>
        /// <param name="imageFormat">The GL image format matching <paramref name="format"/>.</param>
        /// <param name="srgb">Whether the texels are sRGB encoded.</param>
        /// <param name="clearBlock">One compressed block filling the unused part of a layer.</param>
        public ProjectedDecalTextureArray(string label, VTexFormat format, ImageFormat imageFormat, bool srgb, byte[] clearBlock)
        {
            this.label = label;
            this.format = format;
            this.imageFormat = imageFormat;
            this.srgb = srgb;
            this.clearBlock = clearBlock;
        }

        /// <summary>
        /// Adds a texture, or finds it if it was added before. Returns null when the texture cannot be
        /// loaded or is not in this array's format.
        /// </summary>
        public Layer? Add(GameFileLoader fileLoader, string path)
        {
            if (layersByPath.TryGetValue(path, out var existing))
            {
                return existing;
            }

            using var resource = fileLoader.LoadFileCompiled(path);

            if (resource?.DataBlock is not Texture data || data.Format != format || data.Depth != 1)
            {
                return null;
            }

            var layer = new Layer(layerPaths.Count, data.Width, data.Height);
            layerPaths.Add(path);
            layersByPath[path] = layer;

            var width = Math.Max(Width, (int)data.Width);
            var height = Math.Max(Height, (int)data.Height);

            if (ArrayTexture == null || width != Width || height != Height || layer.Index >= capacity)
            {
                var newCapacity = Math.Max(capacity, 8);

                while (newCapacity <= layer.Index)
                {
                    newCapacity *= 2;
                }

                Allocate(fileLoader, width, height, newCapacity, data);
            }
            else
            {
                Upload(layer.Index, data);
            }

            return layer;
        }

        /// <summary>Creates a small empty array if there is none, so the sampler always has one bound.</summary>
        public void EnsureCreated()
        {
            if (ArrayTexture == null)
            {
                Allocate(null, BlockSize, BlockSize, 1, null);
            }
        }

        /// <summary>Releases the array and forgets every texture added.</summary>
        public void Delete()
        {
            ArrayTexture?.Delete();
            ArrayTexture = null;

            Width = 0;
            Height = 0;
            capacity = 0;
            levels = 0;

            layerPaths.Clear();
            layersByPath.Clear();
        }

        // Growing means new storage, so every layer added so far is loaded and uploaded again
        private void Allocate(GameFileLoader? fileLoader, int width, int height, int layerCapacity, Texture? newestData)
        {
            ArrayTexture?.Delete();

            Width = width;
            Height = height;
            capacity = layerCapacity;
            levels = 1 + int.Log2(Math.Max(width, height));

            ArrayTexture = RenderTexture.Create3D(TextureTarget.Texture2DArray, width, height, layerCapacity, imageFormat, levels, label, srgb);
            ArrayTexture.SetFiltering(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
            ArrayTexture.SetWrapMode(RsTextureAddressMode.Clamp);

            if (MaterialLoader.MaxTextureMaxAnisotropy >= 4)
            {
                ArrayTexture.SetMaxAnisotropy(MaterialLoader.MaxTextureMaxAnisotropy);
            }

            for (var i = 0; i < layerPaths.Count; i++)
            {
                if (i == layerPaths.Count - 1 && newestData != null)
                {
                    Upload(i, newestData);
                    continue;
                }

                using var resource = fileLoader?.LoadFileCompiled(layerPaths[i]);

                if (resource?.DataBlock is Texture data)
                {
                    Upload(i, data);
                }
            }
        }

        private void Upload(int layer, Texture data)
        {
            Debug.Assert(ArrayTexture != null);

            var scratch = ArrayPool<byte>.Shared.Rent(GetBlockCount(Width) * GetBlockCount(Height) * clearBlock.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(data.GetBiggestBufferSize());

            byte[]? smallestMip = null;
            var smallestLevel = -1;
            var smallestWidth = 0;
            var smallestHeight = 0;

            try
            {
                // A smaller texture only covers the corner of its layer, so the rest is cleared first
                for (var level = 0; level < levels; level++)
                {
                    var (levelWidth, levelHeight) = GetLevelSize(level);
                    var size = GetBlockCount(levelWidth) * GetBlockCount(levelHeight) * clearBlock.Length;

                    for (var offset = 0; offset < size; offset += clearBlock.Length)
                    {
                        clearBlock.CopyTo(scratch, offset);
                    }

                    GL.CompressedTextureSubImage3D(ArrayTexture.Handle, level, 0, 0, layer, levelWidth, levelHeight, 1, GetPixelFormat(), size, scratch);
                }

                Monitor.Enter(data); // reader lock

                try
                {
                    foreach (var (mip, width, height, _, bufferSize) in data.GetEveryMipLevelTexture(buffer))
                    {
                        var level = (int)mip;

                        if (level >= levels)
                        {
                            continue;
                        }

                        UploadRegion(layer, level, width, height, buffer, scratch);

                        if (level > smallestLevel)
                        {
                            smallestLevel = level;
                            smallestWidth = width;
                            smallestHeight = height;

                            if (level == data.NumMipLevels - 1)
                            {
                                smallestMip = buffer.AsSpan(0, bufferSize).ToArray();
                            }
                        }
                    }
                }
                finally
                {
                    Monitor.Exit(data);
                }

                // Levels past the texture's own chain repeat its smallest image
                if (smallestMip != null)
                {
                    for (var level = smallestLevel + 1; level < levels; level++)
                    {
                        UploadRegion(layer, level, smallestWidth, smallestHeight, smallestMip, scratch);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void UploadRegion(int layer, int level, int sourceWidth, int sourceHeight, byte[] source, byte[] scratch)
        {
            Debug.Assert(ArrayTexture != null);

            var (levelWidth, levelHeight) = GetLevelSize(level);

            // A compressed upload covers whole blocks unless it reaches the edge of the level
            var regionWidth = Math.Min(GetBlockCount(sourceWidth) * BlockSize, levelWidth);
            var regionHeight = Math.Min(GetBlockCount(sourceHeight) * BlockSize, levelHeight);

            var sourceBlocksX = GetBlockCount(sourceWidth);
            var regionBlocksX = GetBlockCount(regionWidth);
            var regionBlocksY = GetBlockCount(regionHeight);
            var blockBytes = clearBlock.Length;

            var upload = source;

            // An image repeated into a level smaller than itself keeps its top left blocks
            if (regionBlocksX < sourceBlocksX || regionBlocksY < GetBlockCount(sourceHeight))
            {
                for (var y = 0; y < regionBlocksY; y++)
                {
                    System.Buffer.BlockCopy(source, y * sourceBlocksX * blockBytes, scratch, y * regionBlocksX * blockBytes, regionBlocksX * blockBytes);
                }

                upload = scratch;
            }

            GL.CompressedTextureSubImage3D(ArrayTexture.Handle, level, 0, 0, layer, regionWidth, regionHeight, 1,
                GetPixelFormat(), regionBlocksX * regionBlocksY * blockBytes, upload);
        }

        private (int Width, int Height) GetLevelSize(int level) => (Math.Max(1, Width >> level), Math.Max(1, Height >> level));

        private PixelFormat GetPixelFormat() => (PixelFormat)imageFormat.ToGLSizedInternalFormat(srgb);

        private static int GetBlockCount(int texels) => (texels + BlockSize - 1) / BlockSize;
    }
}
