using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

internal static class LumpReader<TLump> where TLump : struct
{
    private static readonly int Size = Marshal.SizeOf<TLump>();

    // Structs with array fields or a managed size that differs from the file layout are marshalled one by one.
    private static readonly bool IsBlittable = !RuntimeHelpers.IsReferenceOrContainsReferences<TLump>() && Size == Unsafe.SizeOf<TLump>();

    public static TLump[] ReadLump(byte[] source, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > source.Length - length || length % Size != 0)
        {
            throw new InvalidDataException($"Invalid {typeof(TLump).Name} lump range.");
        }

        if (IsBlittable)
        {
            return MemoryMarshal.Cast<byte, TLump>(source.AsSpan(offset, length)).ToArray();
        }

        var array = new TLump[length / Size];

        if (length == 0)
        {
            return array;
        }

        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);

        try
        {
            var start = handle.AddrOfPinnedObject() + offset;

            for (var i = 0; i < array.Length; i++)
            {
                array[i] = Marshal.PtrToStructure<TLump>(start + (i * Size));
            }
        }
        finally
        {
            handle.Free();
        }

        return array;
    }

    public static TLump[] ReadLumpAt(byte[] source, long offset, int count)
    {
        if (count == 0)
        {
            return [];
        }

        ValidateRange(source.Length, offset, count);
        return ReadLump(source, (int)offset, count * Size);
    }

    public static TLump[] ReadLumpFromStream(Stream stream, int count)
    {
        ValidateRange(stream.Length, stream.Position, count);

        if (count == 0)
        {
            return [];
        }

        if (IsBlittable)
        {
            var array = new TLump[count];
            stream.ReadExactly(MemoryMarshal.AsBytes(array.AsSpan()));
            return array;
        }

        var bytes = new byte[count * Size];
        stream.ReadExactly(bytes);
        return ReadLump(bytes, 0, bytes.Length);
    }

    public static TLump[] ReadLumpAt(Stream stream, long offset, int count)
    {
        ValidateRange(stream.Length, offset, count);
        stream.Position = offset;
        return ReadLumpFromStream(stream, count);
    }

    private static void ValidateRange(long length, long offset, int count)
    {
        if (count < 0 || count > int.MaxValue / Size || offset < 0 || offset > length || (long)count * Size > length - offset)
        {
            throw new InvalidDataException($"Invalid {typeof(TLump).Name} range (offset {offset}, count {count}).");
        }
    }
}
