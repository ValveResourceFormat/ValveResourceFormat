namespace ValveResourceFormat.Serialization.KeyValues
{
    public class KV3File
    {
        // <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
        public KV3ID Encoding { get; private set; }
        public KV3ID Format { get; private set; }

        public KVObject Root { get; private set; }

        public KV3File(
            KVObject root,
            KV3ID? encoding = null,
            KV3ID? format = null)
        {
            Root = root;
            Encoding = encoding ?? KV3IDLookup.Get("text");
            Format = format ?? KV3IDLookup.Get("generic");
        }

        public void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine($"<!-- kv3 encoding:{Encoding} format:{Format} -->");

            if (Format.Name == "vrfunknown")
            {
                writer.WriteLine($"// this format guid is not known to Source 2 Viewer, make a pull request to update KV3IDLookup file");
            }

            Root.Serialize(writer);
        }

        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            WriteText(writer);
            return writer.ToString();
        }
    }
}
