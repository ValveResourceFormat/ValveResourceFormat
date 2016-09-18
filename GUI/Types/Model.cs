using System;
using System.Collections.Generic;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;
using Vector4 = OpenTK.Vector4;

namespace GUI.Types
{
    internal class Model
    {
        private readonly Resource Resource;

        public Model(Resource resource)
        {
            Resource = resource;
        }

        public void LoadMeshes(Renderer.Renderer renderer, string path, Matrix4 transform, Vector4 tintColor, Package currentPackage = null, string skin = null)
        {
            var data = (NTRO)Resource.Blocks[BlockType.DATA];

            var refMeshes = (NTROArray)data.Output["m_refMeshes"];
            var materialGroups = (NTROArray)data.Output["m_materialGroups"];

            for (var i = 0; i < refMeshes.Count; i++)
            {
                var refMesh = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)refMeshes[i]).Value;

                var newResource = FileExtensions.LoadFileByAnyMeansNecessary(refMesh.Name + "_c", path, currentPackage);
                if (newResource == null)
                {
                    Console.WriteLine("unable to load mesh " + refMesh.Name);

                    continue;
                }

                if (!newResource.Blocks.ContainsKey(BlockType.VBIB))
                {
                    Console.WriteLine("Old style model, no VBIB!");

                    continue;
                }

                var skinMaterials = new List<string>();

                if (!string.IsNullOrEmpty(skin))
                {
                    foreach (var materialGroup2 in materialGroups)
                    {
                        var materialGroup = ((NTROValue<NTROStruct>)materialGroup2).Value;

                        if (((NTROValue<string>)materialGroup["m_name"]).Value == skin)
                        {
                            var materials = (NTROArray)materialGroup["m_materials"];

                            foreach (var material in materials)
                            {
                                skinMaterials.Add(((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)material).Value.Name);
                            }

                            break;
                        }
                    }
                }

                renderer.AddMeshObject(new MeshObject
                {
                    Resource = newResource,
                    Transform = transform,
                    TintColor = tintColor,
                    SkinMaterials = skinMaterials
                });

                // TODO: Only first, again.
                break;
            }
        }

        public string[] GetAnimationGroups()
        {
            var data = (NTRO)Resource.Blocks[BlockType.DATA];

            var refAnimGroups = (NTROArray)data.Output["m_refAnimGroups"];

            var refs = refAnimGroups.ToArray<ResourceExtRefList.ResourceReferenceInfo>();
            var paths = new string[refs.Length];

            for (var i = 0; i < refs.Length; i++)
            {
                paths[i] = refs[i].Name;
            }

            return paths;
        }
    }
}
