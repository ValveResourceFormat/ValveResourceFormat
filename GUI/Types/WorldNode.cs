using System;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;
using Vector4 = ValveResourceFormat.ResourceTypes.NTROSerialization.Vector4;
using SteamDatabase.ValvePak;

namespace GUI.Types
{
    internal class WorldNode
    {
        private readonly Resource Resource;

        public WorldNode(Resource resource)
        {
            Resource = resource;
        }

        internal void AddMeshes(Renderer.Renderer renderer, string path, Package package)
        {
            var data = Resource.Blocks[BlockType.DATA] as NTRO;

            // Output is WorldNode_t we need to iterate m_sceneObjects inside it.

            var sceneObjectLayerIndices = (NTROArray)data.Output["m_sceneObjectLayerIndices"];
            var sceneObjects = (NTROArray)data.Output["m_sceneObjects"];
            var i = 0;
            foreach (var entry in sceneObjects)
            {
                var layerIndice = ((NTROValue<byte>)sceneObjectLayerIndices[i]).Value;
                i++;

                // TODO: We want UI for this
                if (layerIndice == 2 || layerIndice == 4)
                {
                    continue;
                }

                // sceneObject is SceneObject_t
                var sceneObject = ((NTROValue<NTROStruct>)entry).Value;
                var renderableModel = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)sceneObject["m_renderableModel"]).Value;
                var transform = (NTROArray)sceneObject["m_vTransform"];

                var matrix = default(Matrix4);

                // what is this
                for (var x = 0; x < 4; x++)
                {
                    var a = ((NTROValue<Vector4>)transform[x]).Value;

                    switch (x)
                    {
                        case 0: matrix.Column0 = new OpenTK.Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 1: matrix.Column1 = new OpenTK.Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 2: matrix.Column2 = new OpenTK.Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 3: matrix.Column3 = new OpenTK.Vector4(a.X, a.Y, a.Z, a.W); break;
                    }
                }

                var tintColorWrongVector = ((NTROValue<Vector4>)sceneObject["m_vTintColor"]).Value;

                OpenTK.Vector4 tintColor;
                if (tintColorWrongVector.W == 0)
                {
                    tintColor = OpenTK.Vector4.One;
                    Console.WriteLine("Ignoring tintColor, it will fuck things up.");
                }
                else
                {
                    tintColor = new OpenTK.Vector4(tintColorWrongVector.X, tintColorWrongVector.Y, tintColorWrongVector.Z, tintColorWrongVector.W);
                }

                if (renderableModel != null)
                {
                    var newResource = FileExtensions.LoadFileByAnyMeansNecessary(renderableModel.Name + "_c", path, package);
                    if (newResource == null)
                    {
                        Console.WriteLine("unable to load model " + renderableModel.Name + "_c");

                        continue;
                    }

                    var modelEntry = new Model(newResource);
                    modelEntry.LoadMeshes(renderer, path, matrix, tintColor, package);
                }

                var renderable = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)sceneObject["m_renderable"]).Value;

                if (renderable != null)
                {
                    var newResource = FileExtensions.LoadFileByAnyMeansNecessary(renderable.Name + "_c", path, package);
                    if (newResource == null)
                    {
                        Console.WriteLine("unable to load renderable " + renderable.Name + "_c");

                        continue;
                    }

                    renderer.AddMeshObject(new MeshObject
                    {
                        Resource = newResource,
                        Transform = matrix,
                        TintColor = tintColor
                    });
                }
            }
        }
    }
}
