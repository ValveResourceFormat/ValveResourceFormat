using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using SteamDatabase.ValvePak;
using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.NTRO;

namespace GUI.Types
{
    internal class RenderModel
    {
        private readonly Model model;

        public RenderModel(Model model)
        {
            this.model = model;
        }

        public void LoadMeshes(Renderer.Renderer renderer, string path, Matrix4 transform, Vector4 tintColor, Package currentPackage = null, string skin = null)
        {
            var data = model.GetModelData();

            var refMeshes = data.GetArray<string>("m_refMeshes");
            var materialGroups = data.GetArray<IKeyValueCollection>("m_materialGroups");

            for (var i = 0; i < refMeshes.Length; i++)
            {
                var refMesh = refMeshes[i];

                var newResource = FileExtensions.LoadFileByAnyMeansNecessary(refMesh + "_c", path, currentPackage);
                if (newResource == null)
                {
                    Console.WriteLine("unable to load mesh " + refMesh);

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
                    SkinMaterials = skinMaterials,
                });

                // TODO: Only first, again.
                break;
            }
        }

        public string[] GetAnimationGroups()
            => model.GetModelData()
                .GetArray<string>("m_refAnimGroups")
                .ToArray();
    }
}
