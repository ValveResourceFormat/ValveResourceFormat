using System.Buffers.Binary;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.IO.ContentFormats.DmxModel;

/// <summary>
/// Gives a datamodel's elements ids derived from the document instead of the random ones the library
/// hands out, so that two saves of one document produce the same bytes.
/// </summary>
internal static class DmxElementIds
{
    /// <summary>
    /// Derives every element id from the document, then saves it. Element ids are written as a fixed
    /// sixteen bytes by the binary codec and as a fixed thirty-six characters by the text codec, so the
    /// emitted length does not depend on their value.
    /// </summary>
    public static void SaveDeterministic(this Datamodel.Datamodel dmx, Stream stream, string encoding, int encodingVersion)
    {
        // TODO REMOVE
        new IdWalk().Assign(dmx);

        dmx.Save(stream, encoding, encodingVersion);
    }

    /// <summary>
    /// Walks a document the way the codecs serialize it: depth first from the root, property-derived
    /// attributes before the rest, first visit of a shared element wins. Each element's id is hashed
    /// from its parent's id, the attribute and array slot it hangs off, its class name, its name and
    /// its visit ordinal. The ordinal makes two elements of one document collide only on a hash
    /// collision, which is rejected rather than written, because a repeated id makes the file
    /// unloadable.
    /// </summary>
    private sealed class IdWalk
    {
        private readonly HashSet<DMElement> visited = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Guid> assigned = [];
        private readonly XxHash128 hasher = new();
        private int ordinal;

        public void Assign(Datamodel.Datamodel dmx)
        {
            if (dmx.Root is not null)
            {
                Visit(dmx.Root, Guid.Empty, dmx.Format, 0);
            }
        }

        private void Visit(DMElement element, Guid parent, string step, int index)
        {
            if (element.Stub || !visited.Add(element))
            {
                return;
            }

            var id = Derive(element, parent, step, index);
            ordinal++;

            if (!assigned.Add(id))
            {
                throw new InvalidDataException($"Derived a repeated DMX element id {id} for '{element.Name}' [{element.ClassName}]");
            }

            element.ID = id;

            foreach (var (name, value) in element.GetAllAttributesForSerialization())
            {
                if (value is DMElement child)
                {
                    Visit(child, id, name, 0);
                }
                else if (value is IList<DMElement> children)
                {
                    for (var i = 0; i < children.Count; i++)
                    {
                        if (children[i] is DMElement item)
                        {
                            Visit(item, id, name, i);
                        }
                    }
                }
            }
        }

        private Guid Derive(DMElement element, Guid parent, string step, int index)
        {
            Span<byte> scalars = stackalloc byte[24];
            parent.TryWriteBytes(scalars);
            BinaryPrimitives.WriteInt32LittleEndian(scalars[16..], index);
            BinaryPrimitives.WriteInt32LittleEndian(scalars[20..], ordinal);
            hasher.Append(scalars);

            AppendString(step);
            AppendString(element.ClassName);
            AppendString(element.Name);

            Span<byte> digest = stackalloc byte[16];
            hasher.GetHashAndReset(digest);
            return new Guid(digest);
        }

        private void AppendString(string value)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
            hasher.Append(length);
            hasher.Append(MemoryMarshal.AsBytes(value.AsSpan()));
        }
    }
}
