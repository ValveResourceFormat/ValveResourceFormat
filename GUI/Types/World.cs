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
    internal class World
    {
        private readonly Resource Resource;

        public World(Resource resource)
        {
            Resource = resource;
        }
        internal void AddObjects(Renderer.Renderer renderer, string path, Package package)
        {
            var data = Resource.Blocks[BlockType.DATA] as NTRO;

            // Output is World_t we need to iterate m_worldNodes inside it.
            var worldNodes = (NTROArray)data.Output["m_worldNodes"];
            var nodeData = ((NTROValue<NTROStruct>)worldNodes[0]).Value; //TODO: Not be 0.

            var worldNode = ((NTROValue<string>)nodeData["m_worldNodePrefix"]).Value;
            if (worldNode != null)
            {
                var newResource = FileExtensions.LoadFileByAnyMeansNecessary(worldNode + ".vwnod_c", path, package);
                if (newResource == null)
                {
                    Console.WriteLine("unable to load model " + worldNode + ".vwnod_c");
                    throw new Exception("WTF");
                }

                var node = new WorldNode(newResource);
                node.AddMeshes(renderer, path, package);
            }

            var entityLumps = (NTROArray)data.Output["m_entityLumps"];
            foreach (var lump in entityLumps)
            {
                var reference = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)lump).Value;

                if (reference != null)
                {
                    var newResource = FileExtensions.LoadFileByAnyMeansNecessary(reference.Name + "_c", path, package);
                    if (newResource == null)
                    {
                        Console.WriteLine("unable to load entity lump " + reference.Name + "_c");

                        continue;
                    }

                    var entityLump = newResource.Blocks[BlockType.DATA] as EntitiyLump;
                    foreach (var entity in entityLump.Datas)
                    {
                        var scale = string.Empty;
                        var position = string.Empty;
                        var angles = string.Empty;
                        var model = string.Empty;
                        foreach (var property in entity)
                        {
                            //metadata
                            switch (property.Item2)
                            {
                                case 3368008710: //World Model
                                    model = property.Item3 as string;
                                    break;
                                case 3827302934: //Position
                                    position = property.Item3 as string;
                                    break;
                                case 3130579663: //Angles
                                    angles = property.Item3 as string;
                                    break;
                                case 432137260: //Scale
                                    scale = property.Item3 as string;
                                    break;
                            }
                        }

                        if (scale != string.Empty && position != string.Empty && angles != string.Empty && model != string.Empty)
                        {
                            //Must be something remotely useful.
                            var scaleTemp = scale.Split(' ');
                            var scaleTemp2 = new float[3];
                            for (var i = 0; i < scaleTemp.Length; i++)
                            {
                                scaleTemp2[i] = float.Parse(scaleTemp[i]);
                            }

                            var scaleMatrix = Matrix4.CreateScale(scaleTemp2[0], scaleTemp2[1], scaleTemp2[2]);

                            //Must be something remotely useful.
                            var angleTemp = angles.Split(' ');
                            var angleTemp2 = new float[3];
                            for (var i = 0; i < angleTemp.Length; i++)
                            {
                                angleTemp2[i] = float.Parse(angleTemp[i]);
                            }

                            var rotationMatrix = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(angleTemp2[2]));
                            rotationMatrix *= Matrix4.CreateRotationY(MathHelper.DegreesToRadians(angleTemp2[0]));
                            rotationMatrix *= Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(angleTemp2[1]));

                            //Must be something remotely useful.
                            var positionTemp = position.Split(' ');
                            var positionTemp2 = new float[3];
                            for (var i = 0; i < positionTemp.Length; i++)
                            {
                                positionTemp2[i] = float.Parse(positionTemp[i]);
                            }

                            var posMatrix = Matrix4.CreateTranslation(positionTemp2[0], positionTemp2[1], positionTemp2[2]);

                            var megaMatrix = scaleMatrix * rotationMatrix * posMatrix;

                            var newEntity = FileExtensions.LoadFileByAnyMeansNecessary(model + "_c", path, package);
                            if (newEntity == null)
                            {
                                Console.WriteLine("unable to load entity " + model + "_c");

                                continue;
                            }

                            var entityModel = new Model(newEntity);
                            entityModel.LoadMeshes(renderer, path, megaMatrix, package);
                        }
                    }
                }
            }
        }
    }
}
