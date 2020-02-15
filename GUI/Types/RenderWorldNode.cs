using System;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types
{
    internal class RenderWorldNode
    {
        private readonly WorldNode worldNode;

        public RenderWorldNode(WorldNode worldNode)
        {
            this.worldNode = worldNode;
        }

        public RenderWorldNode(Resource resource)
        {
            worldNode = new WorldNode(resource);
        }

        internal void AddMeshes(Renderer.Renderer renderer, string path, Package package)
        {
            var data = worldNode.GetData();

            var sceneObjectLayerIndices = data.GetIntegerArray("m_sceneObjectLayerIndices");
            var sceneObjects = data.GetArray("m_sceneObjects");
            var i = 0;

            // Output is WorldNode_t we need to iterate m_sceneObjects inside it
            foreach (var sceneObject in sceneObjects)
            {
                var layerIndex = sceneObjectLayerIndices[i];
                i++;

                // TODO: We want UI for this
                if (layerIndex == 2 || layerIndex == 4)
                {
                    continue;
                }

                // sceneObject is SceneObject_t
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");
                var transform = sceneObject.GetArray("m_vTransform");

                var matrix = Matrix4.Identity;

                // what is this
                for (var x = 0; x < transform.Length; x++)
                {
                    var a = transform[x].ToVector4();

                    switch (x)
                    {
                        case 0: matrix.Column0 = new Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 1: matrix.Column1 = new Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 2: matrix.Column2 = new Vector4(a.X, a.Y, a.Z, a.W); break;
                        case 3: matrix.Column3 = new Vector4(a.X, a.Y, a.Z, a.W); break;
                    }
                }

                var tintColorWrongVector = sceneObject.GetSubCollection("m_vTintColor").ToVector4();

                Vector4 tintColor;
                if (tintColorWrongVector.W == 0)
                {
                    tintColor = Vector4.One;
                    Console.WriteLine("Ignoring tintColor, it will fuck things up.");
                }
                else
                {
                    tintColor = new Vector4(tintColorWrongVector.X, tintColorWrongVector.Y, tintColorWrongVector.Z, tintColorWrongVector.W);
                }

                if (renderableModel != null)
                {
                    var newResource = FileExtensions.LoadFileByAnyMeansNecessary(renderableModel + "_c", path, package);
                    if (newResource == null)
                    {
                        Console.WriteLine("unable to load model " + renderableModel + "_c");

                        continue;
                    }

                    var model = new Model(newResource);
                    //var modelEntry = new RenderModel(model);
                    //modelEntry.LoadMeshes(renderer, path, matrix, tintColor, package);
                }

                var renderable = sceneObject.GetProperty<string>("m_renderable");

                if (renderable != null)
                {
                    var newResource = FileExtensions.LoadFileByAnyMeansNecessary(renderable + "_c", path, package);
                    if (newResource == null)
                    {
                        Console.WriteLine("unable to load renderable " + renderable + "_c");

                        continue;
                    }

                    renderer.AddMeshObject(new MeshObject
                    {
                        Resource = newResource,
                        Transform = matrix,
                        TintColor = tintColor,
                    });
                }
            }
        }
    }
}
