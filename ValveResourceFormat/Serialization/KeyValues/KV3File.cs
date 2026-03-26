using System.IO;
using ValveKeyValue;
using ValveKeyValue.KeyValues3;

namespace ValveResourceFormat.Serialization.KeyValues
{
    /// <summary>
    /// Represents a KeyValues3 file.
    /// </summary>
    public class KV3File
    {
        /// <summary>
        /// Gets the encoding identifier.
        /// </summary>
        public KV3ID Encoding { get; }

        /// <summary>
        /// Gets the format identifier.
        /// </summary>
        public KV3ID Format { get; }

        /// <summary>
        /// Gets the root object.
        /// </summary>
        public KVObject Root { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KV3File"/> class.
        /// </summary>
        public KV3File(
            KVObject root,
            KV3ID? encoding = null,
            KV3ID? format = null)
        {
            Root = root;
            Encoding = encoding ?? KV3IDLookup.Get("text");
            Format = format ?? KV3IDLookup.Get("generic");
        }

        /// <summary>
        /// Parses a KeyValues3 file from the specified stream.
        /// </summary>
        public static KV3File Parse(Stream stream)
        {
            var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues3Text);
            var doc = serializer.Deserialize(stream);
            KV3ID? encoding = doc.Header?.Encoding.Name != null ? new KV3ID(doc.Header.Encoding.Name, doc.Header.Encoding.Id) : null;
            KV3ID? format = doc.Header?.Format.Name != null ? new KV3ID(doc.Header.Format.Name, doc.Header.Format.Id) : null;
            return new KV3File(doc, encoding, format);
        }

        /// <summary>
        /// Parses a KeyValues3 file from the specified filename.
        /// </summary>
        public static KV3File Parse(string filename)
        {
            using var fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            return Parse(fileStream);
        }

        /// <summary>
        /// Writes the KV3 file as text.
        /// </summary>
        public void WriteText(IndentedTextWriter writer)
        {
            writer.Write(ToString());
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the KV3 file as formatted text with encoding and format comments.
        /// </remarks>
        public override string ToString()
        {
            using var ms = new MemoryStream();
            var header = new KVHeader { Encoding = Encoding, Format = Format };
            var doc = new KVDocument(header, Root.Name, Root.Value);
            var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues3Text);
            serializer.Serialize(ms, doc);
            ms.Position = 0;
            using var reader = new StreamReader(ms);
            return reader.ReadToEnd();
        }
    }
}
