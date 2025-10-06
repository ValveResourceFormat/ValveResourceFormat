using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ValveResourceFormat.MapFormats
{
    /// <summary>
    /// Represents a Dota 2 grid navigation file.
    /// </summary>
    public class GridNavFile
    {
        /// <summary>
        /// Flags for grid navigation cells.
        /// </summary>
        [Flags]
        public enum GridNavCellFlags : byte
        {
#pragma warning disable CS1591
            Empty = 0x0,
            Traversable = 0x1,
            Blocked = 0x2,
            HeroBlocking = 0x4,
            CreatureBlocking = 0x8,
            WardBlocking = 0x10
#pragma warning restore CS1591
        }

        /// <summary>
        /// Magic number for grid navigation files.
        /// </summary>
        public const uint MAGIC = 0xFADEBEAD;

        /// <summary>
        /// Gets the size of each grid cell edge.
        /// </summary>
        public float EdgeSize { get; private set; }

        /// <summary>
        /// Gets the X offset of the grid.
        /// </summary>
        public float OffsetX { get; private set; }

        /// <summary>
        /// Gets the Y offset of the grid.
        /// </summary>
        public float OffsetY { get; private set; }

        /// <summary>
        /// Gets the width of the grid.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the height of the grid.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets the minimum X coordinate.
        /// </summary>
        public int MinX { get; private set; }

        /// <summary>
        /// Gets the minimum Y coordinate.
        /// </summary>
        public int MinY { get; private set; }

        /// <summary>
        /// Gets the maximum X coordinate.
        /// </summary>
        public int MaxX { get; private set; }

        /// <summary>
        /// Gets the maximum Y coordinate.
        /// </summary>
        public int MaxY { get; private set; }

        /// <summary>
        /// Gets the grid cell data.
        /// </summary>
        public byte[] Grid { get; private set; } = [];

        /// <summary>
        /// Reads grid navigation data from a stream.
        /// </summary>
        public void Read(Stream input)
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, true);

            var magic = reader.ReadUInt32();
            UnexpectedMagicException.Assert(magic == MAGIC, magic);

            EdgeSize = reader.ReadSingle();
            OffsetX = reader.ReadSingle();
            OffsetY = reader.ReadSingle();

            Width = reader.ReadInt32();
            Height = reader.ReadInt32();
            MinX = reader.ReadInt32();
            MinY = reader.ReadInt32();

            MaxX = MinX + Width - 1;
            MaxY = MinY + Height - 1;

            var expectedCells = Width * Height;
            Grid = reader.ReadBytes(expectedCells);

            Debug.Assert(input.Length - input.Position == 0);
        }

        /// <summary>
        /// Reads grid navigation data from a file.
        /// </summary>
        public void Read(string filename)
        {
            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Read(fs);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine(CultureInfo.InvariantCulture, $"GridNav: {Width}x{Height}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"World bounds: ({MinX * EdgeSize:F1},{MinY * EdgeSize:F1}) to ({MaxX * EdgeSize:F1},{MaxY * EdgeSize:F1})");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Edge size: {EdgeSize}, Offset: ({OffsetX:F1},{OffsetY:F1})");
            sb.AppendLine();

            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var index = y * Width + x;
                    var flags = index < Grid.Length ? (GridNavCellFlags)Grid[index] : GridNavCellFlags.Empty;

                    var symbol = '!';

                    if (flags == GridNavCellFlags.Empty)
                    {
                        symbol = ' ';
                    }
                    else if (flags.HasFlag(GridNavCellFlags.Blocked))
                    {
                        symbol = '#';
                    }
                    else if (flags.HasFlag(GridNavCellFlags.HeroBlocking))
                    {
                        symbol = 'H';
                    }
                    else if (flags.HasFlag(GridNavCellFlags.CreatureBlocking))
                    {
                        symbol = 'C';
                    }
                    else if (flags.HasFlag(GridNavCellFlags.WardBlocking))
                    {
                        symbol = 'W';
                    }
                    else if (flags.HasFlag(GridNavCellFlags.Traversable))
                    {
                        symbol = '.';
                    }

                    sb.Append(symbol);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
