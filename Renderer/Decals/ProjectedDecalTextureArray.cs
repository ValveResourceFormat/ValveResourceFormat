using System.Buffers;
using System.Diagnostics;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Decals
{
    // One array texture per kind of decal texture. A smaller texture fills the corner of its layer.
    internal sealed class ProjectedDecalTextureArray
    {
        public readonly record struct Layer(int Index, int Width, int Height, int MipCount);

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

        public VTexFormat Format => format;

        public RenderTexture? ArrayTexture { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public ProjectedDecalTextureArray(string label, VTexFormat format, ImageFormat imageFormat, bool srgb, byte[] clearBlock)
        {
            this.label = label;
            this.format = format;
            this.imageFormat = imageFormat;
            this.srgb = srgb;
            this.clearBlock = clearBlock;
        }

        public Layer? Add(GameFileLoader fileLoader, string path)
        {
            if (layersByPath.TryGetValue(path, out var existing))
            {
                return existing;
            }

            using var resource = fileLoader.LoadFileCompiled(path);

            // TODO: Compress textures in other formats to BC7 on the GPU instead of rejecting them
            if (resource?.DataBlock is not Texture data || data.Format != format || data.Depth != 1)
            {
                return null;
            }

            var layer = new Layer(layerPaths.Count, data.Width, data.Height, data.NumMipLevels);
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

        public void EnsureCreated()
        {
            if (ArrayTexture == null)
            {
                Allocate(null, BlockSize, BlockSize, 1, null);
            }
        }

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

        // Growing reallocates, so every layer is uploaded again
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
                // Clear what a smaller texture leaves uncovered
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

                // Past its own mip chain, repeat the smallest image
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

            // Uploads cover whole blocks, except at the level edge
            var regionWidth = Math.Min(GetBlockCount(sourceWidth) * BlockSize, levelWidth);
            var regionHeight = Math.Min(GetBlockCount(sourceHeight) * BlockSize, levelHeight);

            var sourceBlocksX = GetBlockCount(sourceWidth);
            var regionBlocksX = GetBlockCount(regionWidth);
            var regionBlocksY = GetBlockCount(regionHeight);
            var blockBytes = clearBlock.Length;

            var upload = source;

            // A repeated image larger than the level keeps its top left blocks
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
