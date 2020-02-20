using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GUI.Utils;
using OpenTK;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer
{
    internal class WorldRenderer : IMeshRenderer
    {
        public World World { get; }

        private readonly VrfGuiContext guiContext;

        private readonly List<WorldNodeRenderer> worldNodeRenderers = new List<WorldNodeRenderer>();
        private readonly List<IRenderer> particleRenderers = new List<IRenderer>();
        private readonly List<ModelRenderer> modelRenderers = new List<ModelRenderer>();

        public WorldRenderer(World world, VrfGuiContext vrfGuiContext)
        {
            World = world;
            guiContext = vrfGuiContext;

            // Do setup
            LoadWorldNodes();
            LoadEntities();

            // TODO: Figure out which animations to play on which model renderers
        }

        public void Render(Camera camera)
        {
            foreach (var renderer in worldNodeRenderers)
            {
                renderer.Render(camera);
            }

            foreach (var renderer in modelRenderers)
            {
                renderer.Render(camera);
            }

            foreach (var renderer in particleRenderers)
            {
                renderer.Render(camera);
            }
        }

        public void Update(float frameTime)
        {
            foreach (var renderer in particleRenderers)
            {
                renderer.Update(frameTime);
            }

            foreach (var renderer in modelRenderers)
            {
                renderer.Update(frameTime);
            }
        }

        private void LoadWorldNodes()
        {
            // Output is World_t we need to iterate m_worldNodes inside it.
            var worldNodes = World.GetWorldNodeNames();
            foreach (var worldNode in worldNodes)
            {
                if (worldNode != null)
                {
                    var newResource = guiContext.LoadFileByAnyMeansNecessary(worldNode + ".vwnod_c");
                    if (newResource == null)
                    {
                        throw new Exception("WTF");
                    }

                    var worldNodeRenderer = new WorldNodeRenderer(new WorldNode(newResource), guiContext);
                    worldNodeRenderers.Add(worldNodeRenderer);
                }
            }
        }

        private void LoadEntities()
        {
            foreach (var lumpName in World.GetEntityLumpNames())
            {
                if (lumpName == null)
                {
                    return;
                }

                var newResource = guiContext.LoadFileByAnyMeansNecessary(lumpName + "_c");

                if (newResource == null)
                {
                    return;
                }

                var entityLump = new EntityLump(newResource);
                LoadEntitiesFromLump(entityLump);
            }
        }

        private void LoadEntitiesFromLump(EntityLump entityLump)
        {
            var childEntities = entityLump.GetChildEntityNames();

            foreach (var childEntityName in childEntities)
            {
                // TODO: Should be controlled in UI with world layers
                if (childEntityName.Contains("_destruction"))
                {
                    continue;
                }

                var newResource = guiContext.LoadFileByAnyMeansNecessary(childEntityName + "_c");

                if (newResource == null)
                {
                    continue;
                }

                LoadEntitiesFromLump(new EntityLump(newResource));
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
                    if (property.Key == EntityLumpKeyLookup.Get("model"))
                    {
                        model = property.Data as string;
                    }
                    else if (property.Key == EntityLumpKeyLookup.Get("origin"))
                    {
                        position = property.Data as string;
                    }
                    else if (property.Key == EntityLumpKeyLookup.Get("angles"))
                    {
                        angles = property.Data as string;
                    }
                    else if (property.Key == EntityLumpKeyLookup.Get("scales"))
                    {
                        scale = property.Data as string;
                    }
                    else if (property.Key == EntityLumpKeyLookup.Get("skin"))
                    {
                        skin = property.Data as string;
                    }
                    else if (property.Key == EntityLumpKeyLookup.Get("rendercolor"))
                    {
                        colour = property.Data as byte[];
                    }
                    else if (property.Key == EntityLumpKeyLookup.Get("classname"))
                    {
                        classname = property.Data as string;
                    }
                    else if (property.Key == EntityLumpKeyLookup.Get("effect_name"))
                    {
                        particle = property.Data as string;
                    }
                    else if (property.Key == EntityLumpKeyLookup.Get("defaultanim"))
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
                    var particleResource = guiContext.LoadFileByAnyMeansNecessary(particle + "_c");

                    if (particleResource != null)
                    {
                        var particleSystem = new ParticleSystem(particleResource);
                        var origin = new System.Numerics.Vector3(positionVector.X, positionVector.Y, positionVector.Z);
                        particleRenderers.Add(new ParticleRenderer.ParticleRenderer(particleSystem, guiContext, origin));
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

                var newEntity = guiContext.LoadFileByAnyMeansNecessary(model + "_c");

                if (newEntity == null)
                {
                    continue;
                }

                var newModel = new Model(newEntity);
                var modelRenderer = new ModelRenderer(newModel, guiContext, false);
                modelRenderer.SetMeshTransform(transformationMatrix);
                modelRenderer.SetTint(objColor);

                modelRenderers.Add(modelRenderer);
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

        public IEnumerable<string> GetSupportedRenderModes()
            => worldNodeRenderers.SelectMany(r => r.GetSupportedRenderModes())
            .Concat(modelRenderers.SelectMany(r => r.GetSupportedRenderModes()))
            .Distinct();

        public void SetRenderMode(string renderMode)
        {
            foreach (var renderer in worldNodeRenderers)
            {
                renderer.SetRenderMode(renderMode);
            }

            foreach (var renderer in modelRenderers)
            {
                renderer.SetRenderMode(renderMode);
            }
        }
    }
}
