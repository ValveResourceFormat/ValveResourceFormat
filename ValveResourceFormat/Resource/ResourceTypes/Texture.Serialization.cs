using System.Diagnostics;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public partial class Texture
    {
        private const ushort CurrentVersion = 1;
        private const int ExtraDataEntrySize = 12;

        private IReadOnlyList<ReadOnlyMemory<byte>>? PixelData;

        private SpritesheetData? SheetData;

        /// <summary>
        /// Creates a texture resource from pixel data that is already in the stored <paramref name="format"/>.
        /// The texture can be decoded and uploaded directly,
        /// reading its pixels from <paramref name="pixelData"/>, which must stay unchanged while the texture is in use.
        /// </summary>
        /// <param name="width">The width of mip level 0 in pixels.</param>
        /// <param name="height">The height of mip level 0 in pixels.</param>
        /// <param name="format">The pixel format of <paramref name="pixelData"/>.</param>
        /// <param name="flags">The texture flags. With <see cref="VTexFlags.CUBE_TEXTURE"/> every mip level has six faces.</param>
        /// <param name="reflectivity">The average color of the texture.</param>
        /// <param name="pixelData">
        /// The pixels of every face of every mip level, from mip level 0 to the smallest.
        /// The faces of a cube texture follow one another within a mip level.
        /// </param>
        /// <param name="sheet">The sprite sheet sequences, if the texture is a sprite sheet, which <see cref="GetSpriteSheetData"/> returns.</param>
        /// <param name="depth">
        /// The number of slices of mip level 0. A <see cref="VTexFlags.VOLUME_TEXTURE"/> halves it with each mip level,
        /// and the pixels of a mip level are its slices one after another.
        /// </param>
        /// <returns>A resource whose data block is the new texture.</returns>
        /// <exception cref="ArgumentException">The pixel data does not fit the dimensions and format.</exception>
        public static Resource Create(
            ushort width,
            ushort height,
            VTexFormat format,
            VTexFlags flags,
            Vector4 reflectivity,
            IReadOnlyList<ReadOnlyMemory<byte>> pixelData,
            SpritesheetData? sheet = null,
            ushort depth = 1)
        {
            ArgumentNullException.ThrowIfNull(pixelData);
            ArgumentOutOfRangeException.ThrowIfZero(depth);

            var faceCount = (flags & VTexFlags.CUBE_TEXTURE) != 0 ? 6 : 1;
            var mipLevels = pixelData.Count / faceCount;

            if (mipLevels == 0 || pixelData.Count % faceCount != 0)
            {
                throw new ArgumentException($"Pixel data must hold {faceCount} face(s) for each mip level.", nameof(pixelData));
            }

            ArgumentOutOfRangeException.ThrowIfGreaterThan(mipLevels, byte.MaxValue);

            var resource = new Resource(ResourceType.Texture, CurrentVersion);
            var texture = new Texture
            {
                Resource = resource,
                Version = CurrentVersion,
                Flags = flags,
                Reflectivity = reflectivity,
                Width = width,
                Height = height,
                Depth = depth,
                Format = format,
                NumMipLevels = (byte)mipLevels,
                PixelData = pixelData,
                SheetData = sheet,
            };

            for (var mip = 0; mip < mipLevels; mip++)
            {
                var faceSize = texture.CalculateBufferSizeForMipLevel((uint)mip) / faceCount;

                for (var face = 0; face < faceCount; face++)
                {
                    if (pixelData[mip * faceCount + face].Length != faceSize)
                    {
                        throw new ArgumentException($"Mip level {mip} face {face} must be {faceSize} bytes.", nameof(pixelData));
                    }
                }
            }

            resource.AddBlock(texture);

            return resource;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Writes the texture header and extra data. The pixels follow the block in the file.
        /// Only textures made with <see cref="Create"/> can be serialized, and their sprite sheet is not written.
        /// </remarks>
        /// <exception cref="NotImplementedException">The texture was read from a file.</exception>
        public override void Serialize(Stream stream)
        {
            if (PixelData == null)
            {
                throw new NotImplementedException("Serializing a texture that was read from a file is not yet supported. If you need this, send us a pull request!");
            }

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(Version);
            writer.Write((ushort)Flags);
            writer.Write(Reflectivity.X);
            writer.Write(Reflectivity.Y);
            writer.Write(Reflectivity.Z);
            writer.Write(Reflectivity.W);
            writer.Write(Width);
            writer.Write(Height);
            writer.Write(Depth);
            writer.Write((byte)Format);
            writer.Write(NumMipLevels);
            writer.Write(Picmip0Res);

            // The table follows this header. Offsets are relative to the field that holds them.
            writer.Write(8u);
            writer.Write((uint)ExtraData.Count);

            var payloadStart = ExtraData.Count * ExtraDataEntrySize;
            var entryIndex = 0;

            foreach (var (type, bytes) in ExtraData)
            {
                var offsetFieldStart = entryIndex * ExtraDataEntrySize + 4;

                writer.Write((uint)type);
                writer.Write((uint)(payloadStart - offsetFieldStart));
                writer.Write((uint)bytes.Length);

                payloadStart += bytes.Length;
                entryIndex++;
            }

            foreach (var bytes in ExtraData.Values)
            {
                writer.Write(bytes);
            }
        }

        internal override void SerializeTrailingData(Stream stream)
        {
            Debug.Assert(PixelData != null);

            var faceCount = FaceCount;

            // The smallest mip level comes first
            for (var mip = NumMipLevels - 1; mip >= 0; mip--)
            {
                for (var face = 0; face < faceCount; face++)
                {
                    stream.Write(PixelData[mip * faceCount + face].Span);
                }
            }
        }
    }
}
