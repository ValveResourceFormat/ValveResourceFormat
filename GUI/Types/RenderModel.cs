using System;
using System.Collections.Generic;
using System.Linq;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

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
            if (model.Resource.ContainsBlockType(BlockType.CTRL))
            {
                var ctrl = model.Resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
                var meshes = ctrl.Data.GetArray("embedded_meshes");

                return;
            }

            var data = model.GetData();

            var refMeshes = data.GetArray<string>("m_refMeshes");
            var materialGroups = data.GetArray("m_materialGroups");

            for (var i = 0; i < refMeshes.Length; i++)
            {
                var refMesh = refMeshes[i];

                var newResource = FileExtensions.LoadFileByAnyMeansNecessary(refMesh + "_c", path, currentPackage);
                if (newResource == null)
                {
                    Console.WriteLine("unable to load mesh " + refMesh);

                    continue;
                }

                if (!newResource.ContainsBlockType(BlockType.VBIB))
                {
                    Console.WriteLine("Old style model, no VBIB!");

                    continue;
                }

                var skinMaterials = new List<string>();

                if (!string.IsNullOrEmpty(skin))
                {
                    foreach (var materialGroup in materialGroups)
                    {
                        if (materialGroup.GetProperty<string>("m_name") == skin)
                        {
                            var materials = materialGroup.GetArray<string>("m_materials");
                            skinMaterials.AddRange(materials);
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
            => model.GetData()
                .GetArray<string>("m_refAnimGroups")
                .ToArray();
    }
}
