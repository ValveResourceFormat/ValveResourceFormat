using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// PC IVP compact-surface PHY data. Each terminal ledge is one authored convex,
/// and each solid is one compound body. Positions are in Source inches.
/// </summary>
internal sealed class PhyFile
{
    internal sealed class Solid
    {
        public int Index { get; init; }
        public Vector3 MassCenter { get; init; }
        public Vector3 RawInertia { get; init; }
        public required List<Vector3[]> Convexes { get; init; }

        /// <summary>The triangles of each convex, three indices into its points per triangle.</summary>
        public required List<int[]> ConvexTriangles { get; init; }

        /// <summary>The material index of each convex, into the material table of the key values, where 0 is the default.</summary>
        public required List<int> ConvexMaterials { get; init; }
    }

    private const int MinSurfaceVersion = 0x100;
    private const int MaxSurfaceVersion = 0x101;
    private const int MoppModelType = 1;

    /// <summary>Where the parts of a convex are stored, for one surface layout.</summary>
    /// <param name="RootOffsetField">The offset of the ledge tree root in the compact surface header.</param>
    /// <param name="RecordSize">The size of the ledge header that precedes its triangles, which is also the size of a triangle.</param>
    /// <param name="EdgeOffset">The offset of the first edge in a triangle.</param>
    /// <param name="EdgeSize">The size of an edge, whose first 16 bits are its start point.</param>
    /// <param name="MaterialShift">The bit position of the material in the word at <paramref name="MaterialOffset"/>.</param>
    /// <param name="MaterialOffset">The offset of the word that holds the material in a triangle.</param>
    private readonly record struct LedgeLayout(int RootOffsetField, int RecordSize, int EdgeOffset, int EdgeSize, int MaterialOffset, int MaterialShift);

    private static readonly LedgeLayout PackedLedges = new(32, 16, 4, 4, 0, 24);
    private static readonly LedgeLayout WideLedges = new(36, 64, 16, 16, 8, 0);

    public int Checksum { get; }
    public Solid[] Solids { get; }
    public string KeyValuesText { get; } = string.Empty;

    /// <summary>Gets how many solids were left out because they are MOPP surfaces, which the game does not load either.</summary>
    public int MoppSolidCount { get; private set; }

    private readonly byte[] data;
    private int rangeStart;
    private int rangeEnd;

    public PhyFile(Stream stream)
    {
        stream.Position = 0;
        data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        rangeEnd = data.Length;

        var headerSize = Int(0);

        if (headerSize < 16)
        {
            throw Error("header is smaller than 16 bytes.");
        }

        Range(0, headerSize);

        Checksum = Int(12);
        Solids = ReadSolids(headerSize, Int(8), data.Length, out var offset);
        KeyValuesText = ReadText(offset, data.Length);
    }

    /// <summary>
    /// Reads the solids of one model of the collision lump of a map, which are laid out like the solids of a PHY file
    /// and followed by their key values.
    /// </summary>
    /// <param name="data">The lump.</param>
    /// <param name="offset">Where the first solid starts.</param>
    /// <param name="solidCount">The number of solids.</param>
    /// <param name="solidsLength">The length of the solids.</param>
    /// <param name="keyValuesLength">The length of the key values that follow the solids.</param>
    public PhyFile(byte[] data, int offset, int solidCount, int solidsLength, int keyValuesLength)
    {
        this.data = data;
        var solidsEnd = offset + solidsLength;

        Solids = ReadSolids(offset, solidCount, solidsEnd, out _);
        KeyValuesText = ReadText(solidsEnd, solidsEnd + keyValuesLength);
    }

    private Solid[] ReadSolids(int offset, int count, int solidsEnd, out int end)
    {
        if (count < 0 || count > ushort.MaxValue || count > (solidsEnd - offset) / 52)
        {
            throw Error("invalid solid count.");
        }

        var solids = new List<Solid>(count);

        for (var i = 0; i < count; i++)
        {
            rangeStart = 0;
            rangeEnd = solidsEnd;

            var size = Int(offset);

            if (size < 48)
            {
                throw Error($"solid {i} is smaller than its header.");
            }

            var solidEnd = Range((long)offset + 4, size) + size;
            var compact = offset + 4;

            if (Int(compact) == 0x59485056) // VPHY
            {
                Range(offset, 32);

                var version = UShort(compact + 4);

                if (version < MinSurfaceVersion || version > MaxSurfaceVersion)
                {
                    throw new UnexpectedMagicException($"Unsupported PHY collision data version, supported versions are 0x{MinSurfaceVersion:X} to 0x{MaxSurfaceVersion:X}", version, nameof(version));
                }

                if (UShort(compact + 6) == MoppModelType)
                {
                    MoppSolidCount++;
                    offset = solidEnd;
                    continue;
                }

                var surfaceSize = Int(offset + 12);
                compact = offset + 32;
                rangeEnd = compact + surfaceSize;
            }
            else
            {
                rangeEnd = solidEnd;
            }

            rangeStart = compact;
            Range(compact, 48);

            var center = Position(compact);
            var inertia = Vector(compact + 12);

            // The byte size is the top 24 bits of the word after the radius. Surfaces that keep it in a word of its own
            // leave those bits clear, move the tree root one word on and use wide ledges
            var packedSize = (uint)Int(compact + 28) >> 8;
            var layout = packedSize == 0 ? WideLedges : PackedLedges;
            var rootOffset = Int(compact + layout.RootOffsetField);

            rangeStart = compact + 48;
            var materials = new List<int>();
            var triangles = new List<int[]>();
            var convexes = ReadTree(Relative(compact, rootOffset, 28), layout, materials, triangles);

            solids.Add(new Solid
            {
                Index = i,
                MassCenter = center,
                RawInertia = inertia,
                Convexes = convexes,
                ConvexTriangles = triangles,
                ConvexMaterials = materials,
            });
            offset = solidEnd;
        }

        end = offset;
        return solids.ToArray();
    }

    private string ReadText(int offset, int end)
    {
        if (offset >= end)
        {
            return string.Empty;
        }

        var textEnd = Array.IndexOf(data, (byte)0, offset, end - offset);
        var length = (textEnd < 0 ? end : textEnd) - offset;

        return Encoding.UTF8.GetString(data, offset, length);
    }

    private List<Vector3[]> ReadTree(int root, LedgeLayout layout, List<int> materials, List<int[]> triangles)
    {
        var pending = new Stack<int>();
        var nodes = new HashSet<int>();
        var convexes = new List<Vector3[]>();
        pending.Push(root);

        while (pending.TryPop(out var node))
        {
            Range(node, 28);

            if (!nodes.Add(node) || nodes.Count > (rangeEnd - rangeStart) / 28)
            {
                throw Error("cyclic, shared or overlapping ledge tree.");
            }

            var right = Int(node);

            if (right != 0)
            {
                pending.Push(Relative(node, right, 28));
                pending.Push(Range((long)node + 28, 28));
                continue;
            }

            var ledgeOffset = Int(node + 4);

            if (ledgeOffset == 0)
            {
                throw Error("terminal ledge-tree node has no convex.");
            }

            var ledge = Relative(node, ledgeOffset, 16);
            var triangleCount = UShort(ledge + 12);
            var flags = (uint)Int(ledge + 8);
            var ledgeSize = (long)(flags >> 8) * 16;

            // A triangle of a mesh is stored as a flat convex of its two sides
            if (triangleCount < 2 || ledgeSize < layout.RecordSize + ((long)triangleCount * layout.RecordSize))
            {
                throw Error("invalid convex triangle count or size.");
            }

            Range(ledge, ledgeSize);

            var pointBase = Relative(ledge, Int(ledge), 16);
            var points = new List<Vector3>();
            var pointIndices = new Dictionary<ushort, int>();
            var convexTriangles = new int[triangleCount * 3];

            for (var triangle = 0; triangle < triangleCount; triangle++)
            {
                var start = ledge + layout.RecordSize + (triangle * layout.RecordSize);

                for (var edge = 0; edge < 3; edge++)
                {
                    var vertex = UShort(start + layout.EdgeOffset + (edge * layout.EdgeSize));

                    if (!pointIndices.TryGetValue(vertex, out var pointIndex))
                    {
                        pointIndex = points.Count;
                        pointIndices.Add(vertex, pointIndex);
                        points.Add(Position(Range((long)pointBase + (vertex * 16L), 16)));
                    }

                    convexTriangles[(triangle * 3) + edge] = pointIndex;
                }
            }

            // In the packed layout the material sits in the top byte of the first word of a triangle, below the virtual flag
            var materialWord = (uint)Int(ledge + layout.RecordSize + layout.MaterialOffset);
            materials.Add((int)((materialWord >> layout.MaterialShift) & 0x7f));
            convexes.Add(points.ToArray());
            triangles.Add(convexTriangles);
        }

        if (convexes.Count == 0)
        {
            throw Error("solid has no terminal convexes.");
        }

        return convexes;
    }

    private static InvalidDataException Error(string message) => new($"PHY {message}");

    private int Range(long offset, long size)
    {
        if (size < 0 || offset < rangeStart || offset > rangeEnd || size > rangeEnd - offset)
        {
            throw Error($"data range {offset}+{size} lies outside its owning structure ({rangeStart}..{rangeEnd}).");
        }

        return (int)offset;
    }

    private int Relative(int start, int offset, int size) => Range((long)start + offset, size);

    private int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Range(offset, 4), 4));

    private ushort UShort(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Range(offset, 2), 2));

    private float Float(int offset) => BitConverter.Int32BitsToSingle(Int(offset));

    private Vector3 Vector(int offset) => new(Float(offset), Float(offset + 4), Float(offset + 8));

    private Vector3 Position(int offset)
    {
        // IVP stores meters with Y down and Z along Source Y.
        var value = Vector(offset);
        return new Vector3(value.X, value.Z, -value.Y) / 0.0254f;
    }
}
