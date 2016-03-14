using System;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace GUI.Types
{
    internal class Model
    {
        private readonly Resource Resource;

        public Model(Resource resource)
        {
            Resource = resource;
        }

        public void GetMesh(Resource newResource, Package currentPackage = null)
        {
            var data = (NTRO)Resource.Blocks[BlockType.DATA];

            var refMeshes = (NTROArray)data.Output["m_refMeshes"];

            // TODO: We're taking first mesh only for now
            var mesh = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)refMeshes[0]).Value;

            if (!Utils.FileExtensions.LoadFileByAnyMeansNecessary(newResource, mesh.Name + "_c", null, currentPackage))
            {
                Console.WriteLine("unable to load mesh " + mesh.Name);
            }
        }

        public string GetAnimationGroup()
        {
            var data = (NTRO)Resource.Blocks[BlockType.DATA];

            var refAnimGroups = (NTROArray)data.Output["m_refAnimGroups"];

            if (refAnimGroups.Count > 0)
            {
                var animGroup = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)refAnimGroups[0]).Value;
                return Utils.FileExtensions.FindResourcePath(animGroup.Name);
            }
            else
            {
                return string.Empty;
            }

        }
    }
}
