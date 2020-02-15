using System;
using System.Collections.Generic;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;

namespace ValveResourceFormat.ResourceTypes
{
    public class Mesh
    {
        public ResourceData Data { get; }

        public ArgumentDependencies MeshArguments { get; }

        public VBIB VBIB { get; }

        public Mesh(Resource resource)
        {
            Data = resource.DataBlock;
            MeshArguments = (ArgumentDependencies)resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.ArgumentDependencies];
            VBIB = resource.VBIB;
        }

        public Mesh(ResourceData data, VBIB vbib)
        {
            Data = data;
            MeshArguments = new ArgumentDependencies();
            VBIB = vbib;
        }
    }
}
