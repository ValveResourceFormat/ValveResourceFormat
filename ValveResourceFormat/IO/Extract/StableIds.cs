using System.IO;
using System.IO.Hashing;
using System.Linq;
using Datamodel;

namespace ValveResourceFormat.IO;

/// <summary>
/// IDs derived from what they identify instead of picked at random, so decompiling the same input twice gives the same bytes.
/// </summary>
internal static class StableIds
{
    /// <summary>
    /// Creates the ID at a position in the sequence of IDs derived from a seed.
    /// </summary>
    public static Guid Create(ReadOnlySpan<byte> seed, long index)
    {
        Span<byte> id = stackalloc byte[16];
        XxHash128.Hash(seed, id, index);
        return new Guid(id);
    }

    /// <summary>
    /// Saves a datamodel with element IDs derived from its contents and the order of its elements.
    /// </summary>
    public static void SaveWithStableIds(this Datamodel.Datamodel datamodel, Stream stream, string encoding, int encodingVersion)
    {
        var elements = GetElements(datamodel);

        using var hashStream = new HashStream();
        AssignIds(datamodel, elements, []);
        datamodel.Save(hashStream, encoding, encodingVersion);

        AssignIds(datamodel, elements, hashStream.GetHash());
        datamodel.Save(stream, encoding, encodingVersion);
    }

    private static void AssignIds(Datamodel.Datamodel datamodel, List<Element> elements, ReadOnlySpan<byte> seed)
    {
        datamodel.PrefixElementId = Create(seed, -1);

        for (var i = 0; i < elements.Count; i++)
        {
            elements[i].ID = Create(seed, i);
        }
    }

    private static List<Element> GetElements(Datamodel.Datamodel datamodel)
    {
        var elements = new List<Element>();
        var visited = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Element>();

        if (datamodel.Root != null)
        {
            pending.Push(datamodel.Root);
        }

        while (pending.TryPop(out var element))
        {
            if (element.Stub || !visited.Add(element))
            {
                continue;
            }

            elements.Add(element);

            var children = new List<Element>();

            foreach (var (_, value) in element.GetAllAttributesForSerialization())
            {
                if (value is Element child)
                {
                    children.Add(child);
                }
                else if (value is IEnumerable<Element> array)
                {
                    children.AddRange(array.Where(static child => child != null));
                }
            }

            for (var i = children.Count - 1; i >= 0; i--)
            {
                pending.Push(children[i]);
            }
        }

        return elements;
    }

    private sealed class HashStream : Stream
    {
        private readonly XxHash128 Hash = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public byte[] GetHash() => Hash.GetCurrentHash();

        public override void Write(byte[] buffer, int offset, int count) => Hash.Append(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => Hash.Append(buffer);
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
