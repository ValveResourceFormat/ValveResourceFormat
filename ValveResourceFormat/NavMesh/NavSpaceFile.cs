using System.Globalization;
using System.IO;
using System.Text;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Represents a navigation space file (<c>.navspace</c>), the volumes flying agents can path through, as convex
    /// blocks bucketed into a uniform grid and the transitions connecting them.
    /// </summary>
    public class NavSpaceFile
    {
        /// <summary>
        /// Magic number for navigation space files.
        /// </summary>
        public const uint MAGIC = 0xFACEFEED;

        /// <summary>
        /// Gets the file version.
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>
        /// Gets the world bounds the grid was built for. The grid starts at its minimum, and the last cells may
        /// extend past its maximum.
        /// </summary>
        public AABB Bounds { get; private set; }

        /// <summary>
        /// Gets the edge length of a grid cell.
        /// </summary>
        public float CellSize { get; private set; }

        /// <summary>
        /// Gets the number of grid cells along each axis.
        /// </summary>
        public (int X, int Y, int Z) GridSize { get; private set; }

        /// <summary>
        /// Gets the blocks of each grid cell. A cell at grid coordinate (x, y, z) is stored at index
        /// <c>x + GridSize.X * (y + GridSize.Y * z)</c>.
        /// </summary>
        public NavSpaceBlock[][] Cells { get; private set; } = [];

        /// <summary>
        /// Gets the transitions between blocks. Only stored in version 2 and newer.
        /// </summary>
        public NavSpaceTransition[] Transitions { get; private set; } = [];

        /// <summary>
        /// Gets every block of the file in id order.
        /// </summary>
        public IEnumerable<NavSpaceBlock> Blocks
        {
            get
            {
                foreach (var cell in Cells)
                {
                    foreach (var block in cell)
                    {
                        yield return block;
                    }
                }
            }
        }

        /// <summary>
        /// Reads the navigation space from a file.
        /// </summary>
        public void Read(string filename)
        {
            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Read(fs);
        }

        /// <summary>
        /// Reads the navigation space from a stream.
        /// </summary>
        public void Read(Stream stream)
        {
            using var binaryReader = new BinaryReader(stream, Encoding.UTF8, true);
            Read(binaryReader);
        }

        /// <summary>
        /// Reads the navigation space from a binary reader.
        /// </summary>
        public void Read(BinaryReader binaryReader)
        {
            var magic = binaryReader.ReadUInt32();
            if (magic != MAGIC)
            {
                throw new UnexpectedMagicException($"Unexpected magic, expected {MAGIC:X}", magic, nameof(magic));
            }

            var version = binaryReader.ReadUInt32();
            if (version < 1 || version > 2)
            {
                throw new UnexpectedMagicException("Unsupported navspace version", version, nameof(version));
            }

            Version = version;

            // Unused byte, always 0
            binaryReader.ReadByte();

            Bounds = ReadAABB(binaryReader);
            CellSize = binaryReader.ReadSingle();
            GridSize = (binaryReader.ReadInt32(), binaryReader.ReadInt32(), binaryReader.ReadInt32());

            var cellCount = binaryReader.ReadInt32();
            if (GridSize.X < 0 || GridSize.Y < 0 || GridSize.Z < 0 || cellCount != (long)GridSize.X * GridSize.Y * GridSize.Z)
            {
                throw new InvalidDataException($"Cell count {cellCount} does not match grid size {GridSize.X}x{GridSize.Y}x{GridSize.Z}");
            }

            Cells = new NavSpaceBlock[cellCount][];

            uint nextBlockId = 1;
            for (var i = 0; i < cellCount; i++)
            {
                Cells[i] = ReadCell(binaryReader, i, ref nextBlockId);
            }

            Transitions = Version >= 2 ? ReadTransitions(binaryReader) : [];
        }

        private static NavSpaceBlock[] ReadCell(BinaryReader binaryReader, int cellIndex, ref uint nextBlockId)
        {
            var blockCount = binaryReader.ReadInt32();
            if (blockCount == 0)
            {
                return [];
            }

            var touchesCellBoundary = binaryReader.ReadBytes(blockCount);

            var positions = new Vector3[blockCount];
            for (var i = 0; i < blockCount; i++)
            {
                positions[i] = ReadVector3(binaryReader);
            }

            var blocks = new NavSpaceBlock[blockCount];
            for (var i = 0; i < blockCount; i++)
            {
                var bounds = ReadAABB(binaryReader);
                var vertexCount = binaryReader.ReadInt32();
                var vertices = new Vector3[vertexCount];

                for (var v = 0; v < vertexCount; v++)
                {
                    vertices[v] = ReadVector3(binaryReader);
                }

                blocks[i] = new NavSpaceBlock
                {
                    Id = nextBlockId++,
                    CellIndex = cellIndex,
                    TouchesCellBoundary = touchesCellBoundary[i] != 0,
                    Position = positions[i],
                    Bounds = bounds,
                    Vertices = vertices,
                };
            }

            return blocks;
        }

        private static NavSpaceTransition[] ReadTransitions(BinaryReader binaryReader)
        {
            var dataSize = binaryReader.ReadInt32();
            var dataEnd = binaryReader.BaseStream.Position + dataSize;

            var transitions = new List<NavSpaceTransition>();
            var groupCount = binaryReader.ReadInt32();

            for (var i = 0; i < groupCount; i++)
            {
                var fromBlockId = binaryReader.ReadUInt32();
                var count = binaryReader.ReadInt32();

                for (var t = 0; t < count; t++)
                {
                    var toBlockId = binaryReader.ReadUInt32();
                    var origin = ReadVector3(binaryReader);
                    var scale = binaryReader.ReadSingle();
                    var vertexCount = binaryReader.ReadInt32();

                    var vertices = new Vector3[vertexCount];
                    var min = new Vector3(float.MaxValue);
                    var max = new Vector3(float.MinValue);

                    for (var v = 0; v < vertexCount; v++)
                    {
                        var quantized = new Vector3(binaryReader.ReadUInt16(), binaryReader.ReadUInt16(), binaryReader.ReadUInt16());
                        var vertex = origin + quantized * scale;

                        vertices[v] = vertex;
                        min = Vector3.Min(min, vertex);
                        max = Vector3.Max(max, vertex);
                    }

                    transitions.Add(new NavSpaceTransition
                    {
                        FromBlockId = fromBlockId,
                        ToBlockId = toBlockId,
                        Bounds = vertexCount > 0 ? new AABB(min, max) : new AABB(origin, origin),
                        Vertices = vertices,
                    });
                }
            }

            binaryReader.BaseStream.Position = dataEnd;

            return [.. transitions];
        }

        private static Vector3 ReadVector3(BinaryReader binaryReader)
            => new(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());

        private static AABB ReadAABB(BinaryReader binaryReader)
            => new(ReadVector3(binaryReader), ReadVector3(binaryReader));

        /// <inheritdoc/>
        /// <remarks>
        /// Returns a formatted summary of the grid, blocks and transitions.
        /// </remarks>
        public override string ToString()
        {
            var nonEmptyCells = 0;
            var blockCount = 0;
            var boundaryBlocks = 0;
            var blockVertices = 0;

            foreach (var cell in Cells)
            {
                if (cell.Length > 0)
                {
                    nonEmptyCells++;
                }

                foreach (var block in cell)
                {
                    blockCount++;
                    blockVertices += block.Vertices.Length;

                    if (block.TouchesCellBoundary)
                    {
                        boundaryBlocks++;
                    }
                }
            }

            var transitionVertices = 0;
            foreach (var transition in Transitions)
            {
                transitionVertices += transition.Vertices.Length;
            }

            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Version: {Version}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Bounds min: {Bounds.Min.X:F3} {Bounds.Min.Y:F3} {Bounds.Min.Z:F3}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Bounds max: {Bounds.Max.X:F3} {Bounds.Max.Y:F3} {Bounds.Max.Z:F3}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Cell size: {CellSize}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Grid size: {GridSize.X} x {GridSize.Y} x {GridSize.Z}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Cells: {Cells.Length} ({nonEmptyCells} with blocks)");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Blocks: {blockCount} ({boundaryBlocks} touching their cell boundary)");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Block hull vertices: {blockVertices}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Transitions: {Transitions.Length}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Transition hull vertices: {transitionVertices}");

            return stringBuilder.ToString();
        }
    }
}
