using System.Buffers.Binary;
using System.IO;

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
    }

    public int Checksum { get; }
    public Solid[] Solids { get; }
    public string KeyValuesText { get; } = string.Empty;

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
        var count = Int(8);

        if (count < 0 || count > ushort.MaxValue || count > (data.Length - headerSize) / 52)
        {
            throw Error("invalid solid count.");
        }

        Solids = new Solid[count];
        var offset = headerSize;

        for (var i = 0; i < count; i++)
        {
            rangeStart = 0;
            rangeEnd = data.Length;

            var size = Int(offset);

            if (size < 48)
            {
                throw Error($"solid {i} is smaller than its header.");
            }

            var end = Range((long)offset + 4, size) + size;
            var compact = offset + 4;

            if (Int(compact) == 0x59485056) // VPHY
            {
                Range(offset, 32);
                var surfaceSize = Int(offset + 12);
                compact = offset + 32;
                rangeEnd = compact + surfaceSize;
            }
            else
            {
                rangeEnd = end;
            }

            rangeStart = compact;
            Range(compact, 48);

            var center = Position(compact);
            var inertia = Vector(compact + 12);
            var rootOffset = Int(compact + 32);

            rangeStart = compact + 48;
            var convexes = ReadTree(Relative(compact, rootOffset, 28));

            Solids[i] = new Solid { Index = i, MassCenter = center, RawInertia = inertia, Convexes = convexes };
            offset = end;
        }

        // TODO: parse the trailing KeyValues text (solid names, masses, ragdoll constraints) once a shared parser exists
        if (offset < data.Length)
        {
            var textEnd = Array.IndexOf(data, (byte)0, offset);
            KeyValuesText = System.Text.Encoding.UTF8.GetString(data, offset, (textEnd < 0 ? data.Length : textEnd) - offset);
        }
    }

    private List<Vector3[]> ReadTree(int root)
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

            if (triangleCount < 4 || ledgeSize < 16L + (triangleCount * 16))
            {
                throw Error("invalid convex triangle count or size.");
            }

            Range(ledge, ledgeSize);

            var pointBase = Relative(ledge, Int(ledge), 16);
            var points = new List<Vector3>();
            var indices = new HashSet<ushort>();

            for (var triangle = 0; triangle < triangleCount; triangle++)
            {
                var start = ledge + 16 + (triangle * 16);

                for (var edge = 0; edge < 3; edge++)
                {
                    var vertex = UShort(start + 4 + (edge * 4));

                    if (indices.Add(vertex))
                    {
                        points.Add(Position(Range((long)pointBase + (vertex * 16L), 16)));
                    }
                }
            }

            convexes.Add(points.ToArray());
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
