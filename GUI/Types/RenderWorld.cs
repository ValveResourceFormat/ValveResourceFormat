using System;
using System.Globalization;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;
using Vector3 = OpenTK.Vector3;
using Vector4 = OpenTK.Vector4;

namespace GUI.Types
{
    internal class RenderWorld
    {
        private readonly World world;

        //private static int anonymousCameraCount;

        public RenderWorld(World world)
        {
            this.world = world;
        }

        internal void AddObjects(GLModelViewerControl glRenderControl, VrfGuiContext vrfGuiContext)
        {
            // Output is World_t we need to iterate m_worldNodes inside it.
            var worldNodes = world.GetWorldNodeNames();
            foreach (var worldNode in worldNodes)
            {
                if (worldNode != null)
                {
                    var newResource = vrfGuiContext.LoadFileByAnyMeansNecessary(worldNode + ".vwnod_c");
                    if (newResource == null)
                    {
                        Console.WriteLine("unable to load model " + worldNode + ".vwnod_c");
                        throw new Exception("WTF");
                    }

                    var renderWorldNode = new RenderWorldNode(newResource);
                    renderWorldNode.AddMeshes(glRenderControl, vrfGuiContext);
                }
            }

            foreach (var lump in world.GetEntityLumpNames())
            {
                LoadEntities(lump, glRenderControl, vrfGuiContext);
            }
        }

        private void LoadEntities(string entityName, GLModelViewerControl glRenderControl, VrfGuiContext vrfGuiContext)
        {
            if (entityName == null)
            {
                return;
            }

            var newResource = vrfGuiContext.LoadFileByAnyMeansNecessary(entityName + "_c");
            if (newResource == null)
            {
                Console.WriteLine("unable to load entity lump " + entityName + "_c");

                return;
            }

            var entityLump = new EntityLump(newResource);
            var childEntities = entityLump.GetChildEntityNames();

            foreach (var childEntityName in childEntities)
            {
                // TODO: Should be controlled in UI with world layers
                if (childEntityName.Contains("_destruction"))
                {
                    continue;
                }

                LoadEntities(childEntityName, glRenderControl, vrfGuiContext);
            }

            var worldEntities = entityLump.GetEntities();

            foreach (var entity in worldEntities)
            {
                var scale = string.Empty;
                var position = string.Empty;
                var angles = string.Empty;
                string model = null;
                var skin = string.Empty;
                var colour = new byte[0];
                var classname = string.Empty;
                string particle = null;
                string animation = null;

                foreach (var property in entity.Properties)
                {
                    if (property.MiscType == EntityLumpKeyLookup.Get("model"))
                    {
                        model = property.Data as string;
                    }
                    else if (property.MiscType == EntityLumpKeyLookup.Get("origin"))
                    {
                        position = property.Data as string;
                    }
                    else if (property.MiscType == EntityLumpKeyLookup.Get("angles"))
                    {
                        angles = property.Data as string;
                    }
                    else if (property.MiscType == EntityLumpKeyLookup.Get("scales"))
                    {
                        scale = property.Data as string;
                    }
                    else if (property.MiscType == EntityLumpKeyLookup.Get("skin"))
                    {
                        skin = property.Data as string;
                    }
                    else if (property.MiscType == EntityLumpKeyLookup.Get("rendercolor"))
                    {
                        colour = property.Data as byte[];
                    }
                    else if (property.MiscType == EntityLumpKeyLookup.Get("classname"))
                    {
                        classname = property.Data as string;
                    }
                    else if (property.MiscType == EntityLumpKeyLookup.Get("effect_name"))
                    {
                        particle = property.Data as string;
                    }
                    else if (property.MiscType == EntityLumpKeyLookup.Get("defaultanim"))
                    {
                        animation = property.Data as string;
                    }
                }

                if (scale == string.Empty || position == string.Empty || angles == string.Empty)
                {
                    continue;
                }

                var isGlobalLight = classname == "env_global_light";
                var isCamera =
                    classname == "info_player_start" ||
                    classname == "worldspawn" ||
                    classname == "sky_camera" ||
                    classname == "point_devshot_camera" ||
                    classname == "point_camera";

                var scaleMatrix = Matrix4.CreateScale(ParseCoordinates(scale));

                var positionVector = ParseCoordinates(position);
                var positionMatrix = Matrix4.CreateTranslation(positionVector);

                var pitchYawRoll = ParseCoordinates(angles);
                var rollMatrix = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(pitchYawRoll.Z)); // Roll
                var pitchMatrix = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(pitchYawRoll.X)); // Pitch
                var yawMatrix = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(pitchYawRoll.Y)); // Yaw

                var rotationMatrix = rollMatrix * pitchMatrix * yawMatrix;
                var transformationMatrix = scaleMatrix * rotationMatrix * positionMatrix;

                if (particle != null)
                {
                    var particleResource = vrfGuiContext.LoadFileByAnyMeansNecessary(particle + "_c");

                    if (particleResource != null)
                    {
                        var particleSystem = new ParticleSystem(particleResource);
                        var particleRenderer = new ParticleRenderer.ParticleRenderer(particleSystem, vrfGuiContext, new System.Numerics.Vector3(positionVector.X, positionVector.Y, positionVector.Z));
                        glRenderControl.AddRenderer(particleRenderer);
                    }

                    continue;
                }

                if (isCamera)
                {
                    if (classname == "worldspawn")
                    {
                        // TODO
                        //glRenderControl.SetDefaultWorldCamera(positionVector);
                    }
                    else
                    {
                        // TODO
                        //glRenderControl.AddCamera(name == string.Empty ? $"{classname} #{anonymousCameraCount++}" : name, transformationMatrix);
                    }

                    continue;
                }
                else if (isGlobalLight)
                {
                    // TODO
                    //glRenderControl.SetWorldGlobalLight(positionVector); // TODO: set light angle

                    continue;
                }
                else if (model == null)
                {
                    continue;
                }

                var objColor = Vector4.One;

                // Parse colour if present
                if (colour.Length == 4)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        objColor[i] = colour[i] / 255.0f;
                    }
                }

                var newEntity = vrfGuiContext.LoadFileByAnyMeansNecessary(model + "_c");
                if (newEntity == null)
                {
                    Console.WriteLine($"unable to load entity {model}_c");

                    continue;
                }

                var newModel = new Model(newEntity);
                var modelRenderer = new ModelRenderer(newModel, vrfGuiContext);
                modelRenderer.SetMeshTransform(transformationMatrix);
                modelRenderer.SetTint(objColor);

                if (animation != null)
                {
                    // TODO: Causes rendering bugs, maybe skeletons or something are missing
                    //modelRenderer.SetAnimation(animation);
                }

                // TODO
                glRenderControl.AddRenderer(modelRenderer);
                //var entityModel = new RenderModel(newModel);
                //entityModel.LoadMeshes(renderer, path, transformationMatrix, objColor, package, skin);
            }
        }

        private static Vector3 ParseCoordinates(string input)
        {
            var vector = default(Vector3);
            var split = input.Split(' ');

            for (var i = 0; i < split.Length; i++)
            {
                vector[i] = float.Parse(split[i], CultureInfo.InvariantCulture);
            }

            return vector;
        }
    }
}
