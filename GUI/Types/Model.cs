using System;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace GUI.Types
{
    class Model
    {
        private readonly Resource Resource;

        public Model(Resource resource)
        {
            Resource = resource;
        }

        public string GetMesh()
        {
            var data = (NTRO)Resource.Blocks[BlockType.DATA];

            var refMeshes = (NTROArray)data.Output["m_refMeshes"];

            // TODO: We're taking first mesh only for now
            var mesh = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)refMeshes[0]).Value;

            return Utils.FileExtensions.FindResourcePath(mesh.Name);
        }
    }
}
