namespace ValveResourceFormat.Serialization.KeyValues
{
    public class KV3File
    {
        // <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
        public string Encoding { get; private set; }
        public string Format { get; private set; }

        public KVObject Root { get; private set; }

        public KV3File(
            KVObject root,
            string encoding = "text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}",
            string format = "generic:version{7412167c-06e9-4698-aff2-e63eb59037e7}")
        {
            Root = root;
            Encoding = encoding;
            Format = format;
        }

        public void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine($"<!-- kv3 encoding:{Encoding} format:{Format} -->");
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
