using ValveResourceFormat;

namespace Decompiler
{
    public class ResourceStat
    {
        public ResourceType Type { get; private set; }
        public ushort Version { get; private set; }
        public uint Count { get; set; }
        public string Info { get; set; }
        public List<string> FilePaths { get; private set; }

        public ResourceStat(Resource resource, string info, string filePath)
        {
            Type = resource.ResourceType;
            Version = resource.Version;
            Count = 1;
            Info = info;
            FilePaths = [filePath];
        }

        public ResourceStat(string info, string filePath)
        {
            Type = ResourceType.Unknown;
            Version = 0;
            Count = 1;
            Info = info;
            FilePaths = [filePath];
        }
    }
}
