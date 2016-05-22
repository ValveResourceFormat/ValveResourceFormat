using System;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;
using Vector4 = ValveResourceFormat.ResourceTypes.NTROSerialization.Vector4;

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

            // Output is WorldNNode_t we need to iterate m_sceneObjects inside it.
            var sceneObjects = (NTROArray)data.Output["m_sceneObjects"];
            var i = 0;
            foreach (var entry in sceneObjects)
            {
                //if (i > 7) break;
                //i++;
                // sceneObject is SceneObject_t
                var sceneObject = ((NTROValue<NTROStruct>)entry).Value;
                var model = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)sceneObject["m_renderableModel"]).Value;
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

                var newResource = new Resource();
                if (!FileExtensions.LoadFileByAnyMeansNecessary(newResource, model.Name + "_c", path, package))
                {
                    Console.WriteLine("unable to load model " + model.Name + "_c");

                    continue;
                }

                var modelEntry = new Model(newResource);
                modelEntry.LoadMeshes(renderer, path, package, matrix);
            }
        }
    }
}
