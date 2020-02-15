using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class Mesh
    {
        public ResourceData Data { get; }

        public VBIB VBIB { get; }

        public Mesh(Resource resource)
        {
            Data = resource.DataBlock;
            VBIB = resource.VBIB;
        }

        public Mesh(ResourceData data, VBIB vbib)
        {
            Data = data;
            VBIB = vbib;
        }
    }
}
