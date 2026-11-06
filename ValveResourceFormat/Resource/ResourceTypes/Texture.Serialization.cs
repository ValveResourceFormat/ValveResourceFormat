using System.Diagnostics;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public partial class Texture
    {
        private const ushort CurrentVersion = 1;
        private const int ExtraDataEntrySize = 12;
        private const int SheetHeaderSize = 8;
        private const int SheetSequenceSize = 32;
        private const int SheetFrameSize = 12;
        private const int SheetFloatParamSize = 8;

        private IReadOnlyList<ReadOnlyMemory<byte>>? PixelData;

        /// <summary>
        /// Creates a texture resource from pixel data that is already in the stored <paramref name="format"/>, ready for
        /// <see cref="ValveResourceFormat.Resource.Serialize"/>. The texture can also be decoded and uploaded directly,
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
        /// <param name="sheet">The sprite sheet sequences, if the texture is a sprite sheet.</param>
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

            if (sheet != null)
            {
                texture.ExtraData.Add(VTexExtraData.SHEET, SerializeSpriteSheetData(sheet));
            }

            resource.AddBlock(texture);

            return resource;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Writes the texture header and extra data. The pixels follow the block in the file.
        /// Only textures made with <see cref="Create"/> can be serialized.
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

        private static byte[] SerializeSpriteSheetData(SpritesheetData sheet)
        {
            var sequences = sheet.Sequences;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(SheetHeaderSize);
            writer.Write((uint)sequences.Length);

            // Offset fields are written as zero and patched once the data they point to is written
            foreach (var sequence in sequences)
            {
                writer.Write(sequence.Id);
                writer.Write(sequence.Clamp);
                writer.Write(sequence.AlphaCrop);
                writer.Write(sequence.NoColor);
                writer.Write(sequence.NoAlpha);
                writer.Write(0);
                writer.Write((uint)sequence.Frames.Length);
                writer.Write(sequence.TotalTime);
                writer.Write(0);
                writer.Write(0);
                writer.Write((uint)sequence.FloatParams.Count);
            }

            for (var s = 0; s < sequences.Length; s++)
            {
                var frames = sequences[s].Frames;
                var headerStart = SheetHeaderSize + s * SheetSequenceSize;

                WriteOffsetTo(writer, headerStart + 8, stream.Position);

                var framesStart = stream.Position;

                foreach (var frame in frames)
                {
                    writer.Write(frame.DisplayTime);
                    writer.Write(0);
                    writer.Write((uint)frame.Images.Length);
                }

                for (var f = 0; f < frames.Length; f++)
                {
                    WriteOffsetTo(writer, framesStart + f * SheetFrameSize + 4, stream.Position);

                    foreach (var image in frames[f].Images)
                    {
                        WriteVector2(writer, image.CroppedMin);
                        WriteVector2(writer, image.CroppedMax);
                        WriteVector2(writer, image.UncroppedMin);
                        WriteVector2(writer, image.UncroppedMax);
                    }
                }
            }

            for (var s = 0; s < sequences.Length; s++)
            {
                WriteOffsetTo(writer, SheetHeaderSize + s * SheetSequenceSize + 20, stream.Position);
                WriteNullTerminatedString(writer, sequences[s].Name);
            }

            for (var s = 0; s < sequences.Length; s++)
            {
                var floatParams = sequences[s].FloatParams;

                if (floatParams.Count == 0)
                {
                    continue;
                }

                WriteOffsetTo(writer, SheetHeaderSize + s * SheetSequenceSize + 24, stream.Position);

                var paramsStart = stream.Position;

                foreach (var value in floatParams.Values)
                {
                    writer.Write(0);
                    writer.Write(value);
                }

                var p = 0;

                foreach (var name in floatParams.Keys)
                {
                    WriteOffsetTo(writer, paramsStart + p * SheetFloatParamSize, stream.Position);
                    WriteNullTerminatedString(writer, name);
                    p++;
                }
            }

            return stream.ToArray();
        }

        // Stores the distance from the offset field to the target, as the sheet reader expects
        private static void WriteOffsetTo(BinaryWriter writer, long fieldPosition, long target)
        {
            var end = writer.BaseStream.Position;

            writer.BaseStream.Position = fieldPosition;
            writer.Write((int)(target - fieldPosition));
            writer.BaseStream.Position = end;
        }

        private static void WriteNullTerminatedString(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte)0);
        }

        private static void WriteVector2(BinaryWriter writer, Vector2 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
        }
    }
}
