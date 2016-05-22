using System;
using GUI.Utils;
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

        public void LoadMeshes(Renderer.Renderer renderer, Package currentPackage = null)
        {
            var data = (NTRO)Resource.Blocks[BlockType.DATA];

            var refMeshes = (NTROArray)data.Output["m_refMeshes"];

            for (var i = 0; i < refMeshes.Count; i++)
            {
                var refMesh = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)refMeshes[i]).Value;

                var newResource = new Resource();
                if (!FileExtensions.LoadFileByAnyMeansNecessary(newResource, refMesh.Name + "_c", null, currentPackage))
                {
                    Console.WriteLine("unable to load mesh " + refMesh.Name);

                    continue;
                }

                if (!newResource.Blocks.ContainsKey(BlockType.VBIB))
                {
                    Console.WriteLine("Old style model, no VBIB!");

                    continue;
                }

                renderer.AddResource(newResource);
            }
        }

        public string GetAnimationGroup()
        {
            var data = (NTRO)Resource.Blocks[BlockType.DATA];

            var refAnimGroups = (NTROArray)data.Output["m_refAnimGroups"];

            if (refAnimGroups.Count > 0)
            {
                var animGroup = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)refAnimGroups[0]).Value;
                return FileExtensions.FindResourcePath(animGroup.Name);
            }

            return string.Empty;
        }
    }
}
