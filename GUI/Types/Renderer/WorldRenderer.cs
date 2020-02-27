// #define DEBUG_OCTREE

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer
{
    internal class WorldRenderer : IMeshRenderer
    {
        private World World { get; }

        public AABB BoundingBox { get; private set; }

        private readonly VrfGuiContext guiContext;

        private readonly List<WorldNodeRenderer> worldNodeRenderers = new List<WorldNodeRenderer>();
        private readonly List<IRenderer> particleRenderers = new List<IRenderer>();
        private readonly List<ModelRenderer> modelRenderers = new List<ModelRenderer>();

        private readonly Octree<IRenderer> staticOctree = new Octree<IRenderer>(32768);
        private readonly Octree<IRenderer> particleOctree = new Octree<IRenderer>(32768);

#if DEBUG_OCTREE
        private readonly OctreeDebugRenderer<IRenderer> octreeDebugRenderer;
#endif

        public WorldRenderer(World world, VrfGuiContext vrfGuiContext)
        {
            World = world;
            guiContext = vrfGuiContext;

            // Do setup
            LoadWorldNodes();
            LoadEntities();

#if DEBUG_OCTREE
            octreeDebugRenderer = new OctreeDebugRenderer<IRenderer>(particleOctree, guiContext, true);
#endif

            // TODO: Figure out which animations to play on which model renderers
        }

        public void Render(Camera camera, RenderPass renderPass)
        {
            // Both world nodes and entity models are rendered through the octree rather than directly
            var meshesToRender = GetMeshesToRender(camera);

            foreach (var renderer in meshesToRender)
            {
                renderer.Render(camera, RenderPass.Opaque);
            }

            // Render translucent items back (furthest) to front (closest)
            for (var i = meshesToRender.Count - 1; i >= 0; i--)
            {
                meshesToRender[i].Render(camera, RenderPass.Translucent);
            }

#if DEBUG_OCTREE
            octreeDebugRenderer.Render(camera, RenderPass.Both);
#endif
        }

        public void Update(float frameTime)
        {
            foreach (var renderer in particleRenderers)
            {
                var oldBounds = renderer.BoundingBox;
                renderer.Update(frameTime);
                particleOctree.Update(renderer, oldBounds, renderer.BoundingBox);
            }

            foreach (var renderer in modelRenderers)
            {
                renderer.Update(frameTime);
            }
        }

        private List<IRenderer> GetMeshesToRender(Camera camera)
        {
            var renderers = staticOctree.Query(camera.ViewFrustum);
            renderers.AddRange(particleOctree.Query(camera.ViewFrustum));

            renderers.Sort((a, b) =>
            {
                var aLength = (a.BoundingBox.Center - camera.Location).LengthSquared();
                var bLength = (b.BoundingBox.Center - camera.Location).LengthSquared();
                return bLength.CompareTo(aLength);
            });

            return renderers;
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

                    var worldNodeRenderer = new WorldNodeRenderer(new WorldNode(newResource), guiContext, staticOctree);
                    worldNodeRenderers.Add(worldNodeRenderer);

                    BoundingBox = BoundingBox.IsZero ? worldNodeRenderer.BoundingBox : BoundingBox.Union(worldNodeRenderer.BoundingBox);
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
                // TODO: Use lump's "m_name" to correlate to the world layer

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
                var scale = entity.GetProperty<string>("scales");
                var position = entity.GetProperty<string>("origin");
                var angles = entity.GetProperty<string>("angles");
                var model = entity.GetProperty<string>("model");
                var skin = entity.GetProperty<string>("skin");
                var colour = entity.GetProperty<byte[]>("rendercolor");
                var classname = entity.GetProperty<string>("classname");
                var particle = entity.GetProperty<string>("effect_name");
                var animation = entity.GetProperty<string>("defaultanim");

                if (scale == null || position == null || angles == null)
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

                var scaleMatrix = Matrix4x4.CreateScale(ParseCoordinates(scale));

                var positionVector = ParseCoordinates(position);
                var positionMatrix = Matrix4x4.CreateTranslation(positionVector);

                var pitchYawRoll = ParseCoordinates(angles);
                var rollMatrix = Matrix4x4.CreateRotationX(OpenTK.MathHelper.DegreesToRadians(pitchYawRoll.Z)); // Roll
                var pitchMatrix = Matrix4x4.CreateRotationY(OpenTK.MathHelper.DegreesToRadians(pitchYawRoll.X)); // Pitch
                var yawMatrix = Matrix4x4.CreateRotationZ(OpenTK.MathHelper.DegreesToRadians(pitchYawRoll.Y)); // Yaw

                var rotationMatrix = rollMatrix * pitchMatrix * yawMatrix;
                var transformationMatrix = scaleMatrix * rotationMatrix * positionMatrix;

                if (particle != null)
                {
                    var particleResource = guiContext.LoadFileByAnyMeansNecessary(particle + "_c");

                    if (particleResource != null)
                    {
                        var particleSystem = new ParticleSystem(particleResource);
                        var origin = new System.Numerics.Vector3(positionVector.X, positionVector.Y, positionVector.Z);

                        var particleRenderer = new ParticleRenderer.ParticleRenderer(particleSystem, guiContext, origin);
                        particleOctree.Insert(particleRenderer, particleRenderer.BoundingBox);
                        particleRenderers.Add(particleRenderer);
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
                    objColor.X = colour[0] / 255.0f;
                    objColor.Y = colour[1] / 255.0f;
                    objColor.Z = colour[2] / 255.0f;
                    objColor.W = colour[3] / 255.0f;
                }

                var newEntity = guiContext.LoadFileByAnyMeansNecessary(model + "_c");

                if (newEntity == null)
                {
                    continue;
                }

                var newModel = new Model(newEntity);
                var modelRenderer = new ModelRenderer(newModel, guiContext, skin, false);
                modelRenderer.SetMeshTransform(transformationMatrix);
                modelRenderer.SetTint(objColor);

                if (animation != default)
                {
                    modelRenderer.LoadAnimation(animation); // Load only this animation
                    modelRenderer.SetAnimation(animation);
                }

                staticOctree.Insert(modelRenderer, modelRenderer.BoundingBox);
                modelRenderers.Add(modelRenderer);

                BoundingBox = BoundingBox.IsZero ? modelRenderer.BoundingBox : BoundingBox.Union(modelRenderer.BoundingBox);
            }
        }

        private static Vector3 ParseCoordinates(string input)
        {
            var split = input.Split(' ');

            if (split.Length == 3)
            {
                return new Vector3(
                    float.Parse(split[0], CultureInfo.InvariantCulture),
                    float.Parse(split[1], CultureInfo.InvariantCulture),
                    float.Parse(split[2], CultureInfo.InvariantCulture));
            }

            return default(Vector3);
        }

        public IEnumerable<string> GetWorldLayerNames()
            => worldNodeRenderers.SelectMany(r => r.GetWorldLayerNames());

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

        public void SetWorldLayers(IEnumerable<string> worldLayers)
        {
            foreach (var renderer in worldNodeRenderers)
            {
                renderer.SetWorldLayers(worldLayers);
            }
        }
    }
}
