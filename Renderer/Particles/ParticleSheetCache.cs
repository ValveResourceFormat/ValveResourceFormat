using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// One texture shared by every particle renderer that holds the sheet sequences of every texture
    /// they draw, resampled into rows the spritecard vertex shader looks frames up in.
    /// </summary>
    /// <remarks>
    /// Each sequence takes three rows of <see cref="Columns"/> texels, laid out as pairs of texels that
    /// each stand for one sample of the sequence's timeline:
    /// <list type="bullet">
    /// <item>The first row holds, in the first texel of a pair, the blend from frame A to frame B at
    /// that sample, the sequence length in sixteenths of a second, whether it clamps, and the blend at
    /// the next sample when that sample is still on frame A. The second texel is unused.</item>
    /// <item>The second row holds frame A's tile narrowed to the window both frames' art covers, then
    /// that window in tile units.</item>
    /// <item>The third row holds frame B's tile narrowed to the same window.</item>
    /// </list>
    /// Only the first image of a frame is kept. Rows are handed out on first use and kept for the life
    /// of the cache, which grows when it runs out of rows.
    /// </remarks>
    internal sealed class ParticleSheetCache
    {
        /// <summary>How many texels wide every row is.</summary>
        public const int Columns = 512;

        private const int PairsPerRow = Columns / 2;
        private const int RowsPerSequence = 3;
        private const int ChannelsPerTexel = 4;
        private const int MinimumRows = 3 * 64;

        /// <summary>How many samples a sequence of more than one frame is resampled into before every fourth is kept.</summary>
        private const int SampleCount = 1024;

        private static readonly Texture.SpritesheetData.Sequence.Frame.Image EmptyImage = new();

        private readonly Dictionary<Texture.SpritesheetData, SheetRows> rowsBySheet = [];
        private ushort[] texels = [];
        private int usedRows;

        /// <summary>The texture the rows live in, or null until a sheet has been added.</summary>
        public RenderTexture? Texture { get; private set; }

        /// <summary>Where a texture's sequences start in the cache and how many it has.</summary>
        /// <param name="FirstRow">The row of the first sequence; sequence n starts 3 n rows later.</param>
        /// <param name="SequenceCount">How many sequences the sheet has, or 0 for a texture without one.</param>
        public readonly record struct SheetRows(int FirstRow, int SequenceCount);

        /// <summary>
        /// The rows of the sheet <paramref name="texture"/> carries, resampling and uploading it the first
        /// time it is asked for.
        /// </summary>
        public SheetRows GetRows(RenderTexture texture)
        {
            if (texture.SpriteSheetData is not { Sequences.Length: > 0 } sheet)
            {
                return default;
            }

            if (rowsBySheet.TryGetValue(sheet, out var rows))
            {
                return rows;
            }

            rows = Add(sheet);
            rowsBySheet.Add(sheet, rows);

            return rows;
        }

        private SheetRows Add(Texture.SpritesheetData sheet)
        {
            var firstRow = usedRows;
            var rowCount = sheet.Sequences.Length * RowsPerSequence;

            var grown = EnsureCapacity(firstRow + rowCount);

            for (var sequence = 0; sequence < sheet.Sequences.Length; sequence++)
            {
                WriteSequence(sheet.Sequences[sequence], firstRow + (sequence * RowsPerSequence));
            }

            usedRows += rowCount;

            if (grown)
            {
                Upload(0, usedRows);
            }
            else
            {
                Upload(firstRow, rowCount);
            }

            return new SheetRows(firstRow, sheet.Sequences.Length);
        }

        /// <summary>Makes room for <paramref name="rows"/> rows, returning whether the texture was replaced.</summary>
        private bool EnsureCapacity(int rows)
        {
            var height = Texture?.Height ?? 0;

            if (rows <= height)
            {
                return false;
            }

            var newHeight = Math.Max(MinimumRows, height * 2);

            while (newHeight < rows)
            {
                newHeight *= 2;
            }

            Array.Resize(ref texels, newHeight * Columns * ChannelsPerTexel);

            Texture?.Delete();
            Texture = RenderTexture.Create(Columns, newHeight, ImageFormat.RGBA16161616, "Particle sheet cache");
            Texture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

            return true;
        }

        private void Upload(int firstRow, int rowCount)
        {
            if (Texture == null || rowCount == 0)
            {
                return;
            }

            var start = firstRow * Columns * ChannelsPerTexel;

            GL.TextureSubImage2D(Texture.Handle, 0, 0, firstRow, Columns, rowCount, PixelFormat.Rgba, PixelType.UnsignedShort, ref texels[start]);
        }

        /// <summary>One sample of a sequence's timeline: the two frames it sits between and how far it has crossed.</summary>
        private readonly record struct Sample(int FrameA, int FrameB, float Blend);

        private void WriteSequence(Texture.SpritesheetData.Sequence sequence, int row)
        {
            var frames = sequence.Frames;
            var clamp = sequence.Clamp;
            var singleFrame = frames.Length == 1;

            // A clamped timeline spans one sample fewer, so its last sample lands on the end
            var timelineScale = clamp ? SampleCount - 1f : SampleCount;
            var totalTime = sequence.TotalTime > 0f ? sequence.TotalTime : 1f;

            var startTimes = new float[frames.Length];
            var elapsed = 0f;

            for (var frame = 0; frame < frames.Length; frame++)
            {
                startTimes[frame] = elapsed / totalTime * timelineScale;
                elapsed += frames[frame].DisplayTime;
            }

            var length = frames.Length > 0 ? elapsed : 1f;

            // The length is kept in sixteenths of a second and wraps past 4096 seconds
            var lengthTexel = (ushort)(int)(length * 16f);
            var clampTexel = clamp ? ushort.MaxValue : (ushort)0;

            for (var pair = 0; pair < PairsPerRow; pair++)
            {
                var sample = Resample(startTimes, timelineScale, PairSample(pair, clamp, singleFrame), clamp);

                // The blend at the next sample is only kept while that sample is still on frame A, and
                // a looping sequence never looks past its last pair
                var nextBlend = ushort.MaxValue;

                if (pair < PairsPerRow - 1 || clamp)
                {
                    var next = Resample(startTimes, timelineScale, PairSample(pair + 1, clamp, singleFrame), clamp);

                    if (next.FrameA == sample.FrameA)
                    {
                        nextBlend = ToUnorm16(next.Blend);
                    }
                }

                SetTexel(row, pair * 2, ToUnorm16(sample.Blend), lengthTexel, clampTexel, nextBlend);

                if (frames.Length == 0)
                {
                    continue;
                }

                var imageA = FirstImage(frames[sample.FrameA]);
                var imageB = FirstImage(frames[sample.FrameB]);
                var windowA = CropWindow(imageA);
                var windowB = CropWindow(imageB);
                var windowMin = Vector2.Min(windowA.Min, windowB.Min);
                var windowMax = Vector2.Max(windowA.Max, windowB.Max);

                SetRect(row + 1, pair * 2, NarrowTile(imageA, windowMin), NarrowTile(imageA, windowMax));
                SetRect(row + 1, (pair * 2) + 1, windowMin, windowMax);
                SetRect(row + 2, pair * 2, NarrowTile(imageB, windowMin), NarrowTile(imageB, windowMax));
            }
        }

        /// <summary>
        /// The sample a texel pair stands for. Every fourth of the 1024 samples is kept; a looping
        /// sequence wraps past the last one and a clamped one holds it.
        /// </summary>
        private static int PairSample(int pair, bool clamp, bool singleFrame)
        {
            if (singleFrame)
            {
                return 0;
            }

            var sample = (int)((float)(1.0 / PairsPerRow) * pair * SampleCount);

            return clamp ? Math.Min(sample, SampleCount - 1) : sample & (SampleCount - 1);
        }

        /// <summary>
        /// The frames sample <paramref name="sample"/> sits between, given each frame's start on the
        /// sampled timeline, and how far it has crossed from the first to the second.
        /// </summary>
        /// <remarks>
        /// From the start of the last frame on, a clamped sequence keeps the second to last frame as
        /// frame A with a full blend to the last, so a draw that does not blend frames never shows the
        /// last frame.
        /// </remarks>
        private static Sample Resample(float[] startTimes, float timelineScale, int sample, bool clamp)
        {
            var count = startTimes.Length;
            var started = 0;

            while (started < count && startTimes[started] <= sample)
            {
                started++;
            }

            var last = count - 1;

            if (started == 0)
            {
                return new Sample(0, count > 1 ? 1 : 0, 0f);
            }

            if (started < count)
            {
                var from = started - 1;
                var blend = (sample - startTimes[from]) / (startTimes[started] - startTimes[from]);

                return new Sample(from, started, blend);
            }

            if (clamp)
            {
                return new Sample(count > 1 ? count - 2 : last, last, 1f);
            }

            var wrapBlend = (sample - startTimes[last]) / (timelineScale - startTimes[last] + startTimes[0]);

            return new Sample(last, 0, wrapBlend);
        }

        private static Texture.SpritesheetData.Sequence.Frame.Image FirstImage(Texture.SpritesheetData.Sequence.Frame frame)
            => frame.Images.Length > 0 ? frame.Images[0] : EmptyImage;

        /// <summary>The part of its tile a frame's cropped art covers, in tile units.</summary>
        private static (Vector2 Min, Vector2 Max) CropWindow(Texture.SpritesheetData.Sequence.Frame.Image image)
        {
            var size = image.UncroppedMax - image.UncroppedMin;

            return (
                (image.CroppedMin - image.UncroppedMin) / size,
                (image.CroppedMax - image.UncroppedMin) / size);
        }

        /// <summary>A point of a frame's tile given in tile units, in atlas coordinates.</summary>
        private static Vector2 NarrowTile(Texture.SpritesheetData.Sequence.Frame.Image image, Vector2 tilePoint)
            => image.UncroppedMin + (tilePoint * (image.UncroppedMax - image.UncroppedMin));

        /// <summary>Stores a value the texture reads back as unsigned normalized, truncated and not clamped.</summary>
        private static ushort ToUnorm16(float value) => (ushort)(int)(value * ushort.MaxValue);

        private void SetRect(int row, int column, Vector2 min, Vector2 max)
            => SetTexel(row, column, ToUnorm16(min.X), ToUnorm16(min.Y), ToUnorm16(max.X), ToUnorm16(max.Y));

        private void SetTexel(int row, int column, ushort x, ushort y, ushort z, ushort w)
        {
            var start = ((row * Columns) + column) * ChannelsPerTexel;

            texels[start] = x;
            texels[start + 1] = y;
            texels[start + 2] = z;
            texels[start + 3] = w;
        }
    }
}
