using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class WorldNode
    {
        private readonly Resource resource;

        public WorldNode(Resource resource)
        {
            this.resource = resource;
        }

        public IKeyValueCollection GetData()
        {
            return resource.Blocks[BlockType.DATA] is NTRO ntro
                ? ntro.Output as IKeyValueCollection
                : ((BinaryKV3)resource.Blocks[BlockType.DATA]).Data;
        }
    }
}
