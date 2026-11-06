using System.IO;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.GoldSrc;

/// <summary>Converts GoldSrc sprites to in-memory Source 2 textures.</summary>
internal static class GoldSrcSpriteConverter
{
    private const int FramePadding = 1;

    /// <summary>Converts a sprite to a texture whose frames are the sequence of its sprite sheet.</summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns>A resource whose data block is the texture.</returns>
    /// <remarks>
    /// The frames are laid out on a grid, each with a border of its own edge pixels so that filtering stays inside it.
    /// The cropped rectangle of a frame is its pixels. The uncropped rectangle is the logical frame, the union of
    /// all frames placed by their origins, positioned so that the pixels sit at the offset given by the origin.
    /// </remarks>
    public static Resource Convert(SprFile sprite)
    {
        var frames = sprite.Frames;
        var layout = new SheetLayout(sprite);
        var width = layout.Width;
        var height = layout.Height;

        if (width > ushort.MaxValue || height > ushort.MaxValue)
        {
            throw new InvalidDataException("Sprite frames do not fit in one texture.");
        }

        var atlas = new byte[width * height * 4];
        var sheetFrames = new Texture.SpritesheetData.Sequence.Frame[frames.Length];

        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            var (left, top) = layout.GetFramePosition(i);

            CopyFrame(atlas, width, left, top, frame, sprite.ToRgba(i), layout.Padding);

            var (croppedMin, croppedMax) = layout.GetCroppedRect(i);
            var (uncroppedMin, uncroppedMax) = layout.GetUncroppedRect(i);

            sheetFrames[i] = new Texture.SpritesheetData.Sequence.Frame
            {
                DisplayTime = 1f,
                Images =
                [
                    new Texture.SpritesheetData.Sequence.Frame.Image
                    {
                        CroppedMin = croppedMin,
                        CroppedMax = croppedMax,
                        UncroppedMin = uncroppedMin,
                        UncroppedMax = uncroppedMax,
                    },
                ],
            };
        }

        var sheet = new Texture.SpritesheetData
        {
            Sequences =
            [
                new Texture.SpritesheetData.Sequence
                {
                    Name = "CDmeSheetSequence",
                    TotalTime = frames.Length,
                    Frames = sheetFrames,
                },
            ],
        };

        var flags = VTexFlags.SUGGEST_CLAMPS | VTexFlags.SUGGEST_CLAMPT;

        return Texture.Create((ushort)width, (ushort)height, VTexFormat.RGBA8888, flags, Vector4.Zero, [atlas], sheet);
    }

    /// <summary>Gets where a frame of a sprite is in the texture the sprite converts to.</summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="frame">The index of the frame.</param>
    /// <returns>The texture coordinates of the corners of the frame, inset by half a texel so that filtering stays inside it.</returns>
    public static (Vector2 Min, Vector2 Max) GetFrameRect(SprFile sprite, int frame) => new SheetLayout(sprite).GetFrameRect(frame);

    // The frames sit on a near-square grid of cells the size of the largest frame
    private readonly struct SheetLayout
    {
        private readonly SprFrame[] frames;
        private readonly int columns;
        private readonly int cellWidth;
        private readonly int cellHeight;
        private readonly int logicalLeft;
        private readonly int logicalTop;
        private readonly int logicalWidth;
        private readonly int logicalHeight;

        public int Padding { get; }
        public int Width { get; }
        public int Height { get; }

        public SheetLayout(SprFile sprite)
        {
            frames = sprite.Frames;
            Padding = frames.Length > 1 ? FramePadding : 0;

            var logicalRight = int.MinValue;
            var logicalBottom = int.MaxValue;
            logicalLeft = int.MaxValue;
            logicalTop = int.MinValue;

            foreach (var frame in frames)
            {
                logicalLeft = Math.Min(logicalLeft, frame.OriginX);
                logicalTop = Math.Max(logicalTop, frame.OriginY);
                logicalRight = Math.Max(logicalRight, frame.OriginX + frame.Width);
                logicalBottom = Math.Min(logicalBottom, frame.OriginY - frame.Height);

                cellWidth = Math.Max(cellWidth, frame.Width + (Padding * 2));
                cellHeight = Math.Max(cellHeight, frame.Height + (Padding * 2));
            }

            logicalWidth = logicalRight - logicalLeft;
            logicalHeight = logicalTop - logicalBottom;

            columns = (int)Math.Ceiling(Math.Sqrt(frames.Length));
            var rows = (frames.Length + columns - 1) / columns;
            Width = columns * cellWidth;
            Height = rows * cellHeight;
        }

        public (int Left, int Top) GetFramePosition(int index)
        {
            var left = ((index % columns) * cellWidth) + Padding;
            var top = ((index / columns) * cellHeight) + Padding;

            return (left, top);
        }

        public (Vector2 Min, Vector2 Max) GetCroppedRect(int index)
        {
            var frame = frames[index];
            var (left, top) = GetFramePosition(index);
            var size = new Vector2(Width, Height);

            return (new Vector2(left, top) / size, new Vector2(left + frame.Width, top + frame.Height) / size);
        }

        // The logical frame can reach outside of the texture, where the frame is smaller than the union of the frames
        public (Vector2 Min, Vector2 Max) GetUncroppedRect(int index)
        {
            var frame = frames[index];
            var (left, top) = GetFramePosition(index);
            var size = new Vector2(Width, Height);

            var min = new Vector2(left - (frame.OriginX - logicalLeft), top - (logicalTop - frame.OriginY));
            var max = min + new Vector2(logicalWidth, logicalHeight);

            return (min / size, max / size);
        }

        public (Vector2 Min, Vector2 Max) GetFrameRect(int index)
        {
            var frame = frames[index];
            var (left, top) = GetFramePosition(index);
            var size = new Vector2(Width, Height);

            var min = new Vector2(left + 0.5f, top + 0.5f) / size;
            var max = new Vector2(left + frame.Width - 0.5f, top + frame.Height - 0.5f) / size;

            return (min, max);
        }
    }

    private static void CopyFrame(byte[] atlas, int atlasWidth, int left, int top, SprFrame frame, byte[] rgba, int padding)
    {
        for (var y = -padding; y < frame.Height + padding; y++)
        {
            var sourceY = Math.Clamp(y, 0, frame.Height - 1);

            for (var x = -padding; x < frame.Width + padding; x++)
            {
                var sourceX = Math.Clamp(x, 0, frame.Width - 1);
                var source = ((sourceY * frame.Width) + sourceX) * 4;
                var target = (((top + y) * atlasWidth) + left + x) * 4;

                Array.Copy(rgba, source, atlas, target, 4);
            }
        }
    }
}
