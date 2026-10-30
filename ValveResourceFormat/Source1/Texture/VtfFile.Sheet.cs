using System.Buffers.Binary;
using System.IO;
using VrfTexture = ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Source1.Texture;

internal sealed partial class VtfFile
{
    private const uint SheetResourceType = 0x10;
    private const int MaxSheetSequences = 64;
    private const int ImagesPerFrameVersionZero = 1;
    private const int ImagesPerFrame = 4;

    private byte[]? sheetData;

    /// <summary>
    /// Gets the animation frames of a sprite sheet texture, or <see langword="null"/> when the file has no sheet resource.
    /// </summary>
    public VrfTexture.SpritesheetData? GetSpriteSheetData()
    {
        if (sheetData == null)
        {
            return null;
        }

        try
        {
            return ParseSheet(sheetData);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] ReadSheetResource(Stream stream)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        stream.ReadExactly(lengthBytes);

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        if (length < 0 || (stream.CanSeek && length > stream.Length - stream.Position))
        {
            throw new InvalidDataException("VTF sheet resource is truncated or too large.");
        }

        var data = new byte[length];
        stream.ReadExactly(data);

        return data;
    }

    private static VrfTexture.SpritesheetData ParseSheet(byte[] data)
    {
        var position = 0;

        int ReadInt()
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position));
            position += 4;
            return value;
        }

        float ReadFloat()
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(position));
            position += 4;
            return value;
        }

        var version = ReadInt();
        var imagesOnDisk = version == 0 ? ImagesPerFrameVersionZero : ImagesPerFrame;
        var sequenceCount = ReadInt();

        if (sequenceCount < 0 || sequenceCount > MaxSheetSequences)
        {
            throw new InvalidDataException("VTF sheet has an invalid sequence count.");
        }

        var sequences = new List<VrfTexture.SpritesheetData.Sequence>(sequenceCount);

        for (var i = 0; i < sequenceCount; i++)
        {
            var id = ReadInt();
            var clamp = ReadInt() != 0;
            var frameCount = ReadInt();
            var totalTime = ReadFloat();

            if (frameCount < 0 || frameCount > data.Length)
            {
                throw new InvalidDataException("VTF sheet has an invalid frame count.");
            }

            var frames = new VrfTexture.SpritesheetData.Sequence.Frame[frameCount];

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var duration = ReadFloat();
                var images = new VrfTexture.SpritesheetData.Sequence.Frame.Image[imagesOnDisk];

                for (var imageIndex = 0; imageIndex < imagesOnDisk; imageIndex++)
                {
                    var min = new Vector2(ReadFloat(), ReadFloat());
                    var max = new Vector2(ReadFloat(), ReadFloat());

                    images[imageIndex] = new VrfTexture.SpritesheetData.Sequence.Frame.Image
                    {
                        CroppedMin = min,
                        CroppedMax = max,
                        UncroppedMin = min,
                        UncroppedMax = max,
                    };
                }

                frames[frameIndex] = new VrfTexture.SpritesheetData.Sequence.Frame { DisplayTime = duration, Images = images };
            }

            sequences.Add(new VrfTexture.SpritesheetData.Sequence
            {
                Id = (uint)id,
                Name = "CDmeSheetSequence",
                Clamp = clamp,
                TotalTime = totalTime,
                Frames = frames,
            });
        }

        return new VrfTexture.SpritesheetData { Sequences = [.. sequences] };
    }
}
