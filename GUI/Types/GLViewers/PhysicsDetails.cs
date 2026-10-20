using System.Globalization;
using System.Linq;
using GUI.Forms;
using ValveResourceFormat.Renderer.Editor.Collision;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Fills the details window with the collision group a physics node draws, and the physics of an entity's model.
    /// </summary>
    static class PhysicsDetails
    {
        public static void AddNodeDetails(EntityInfoControl info, PhysSceneNode node)
        {
            if (node.Name != null)
            {
                info.AddProperty("Physics File", node.Name, new LinkedFile(node.Name));
            }

            info.AddProperty("Collision", string.Join(", ", node.CollisionLabels));

            AddGroup(info, node.Physics, node.CollisionAttributeIndex);
        }

        public static void AddEntityDetails(EntityInfoControl info, BaseModelEntity entity, PhysAggregateData physics)
        {
            info.AddProperty("Solid Type", entity.Solid.ToString());

            var mass = 0f;

            foreach (var part in physics.Parts)
            {
                mass += part.Mass;
            }

            if (mass > 0f)
            {
                info.AddProperty("Physics Mass", mass.ToString("G6", CultureInfo.InvariantCulture));
            }

            if (physics.Joints.Length > 0)
            {
                info.AddProperty("Physics Joints", physics.Joints.Length.ToString(CultureInfo.InvariantCulture));
            }

            for (var i = 0; i < physics.CollisionAttributes.Count; i++)
            {
                AddGroup(info, physics, i);
            }
        }

        private static void AddGroup(EntityInfoControl info, PhysAggregateData physics, int attributeIndex)
        {
            var attributes = physics.CollisionAttributes[attributeIndex];

            info.AddProperty("Collision Group", attributes.GetStringProperty("m_CollisionGroupString") ?? string.Empty);
            AddTags(info, "Interact As", PhysAggregateData.GetInteractAsTags(attributes));
            AddTags(info, "Interact With", attributes.GetArray<string>("m_InteractWithStrings") ?? []);
            AddTags(info, "Interact Exclude", attributes.GetArray<string>("m_InteractExcludeStrings") ?? []);

            var surfaceProperties = new HashSet<uint>();
            var surfaceHashes = physics.SurfacePropertyHashes;
            var spheres = 0;
            var capsules = 0;
            var hulls = 0;
            var meshes = 0;
            var meshTriangles = 0;

            foreach (var part in physics.Parts)
            {
                foreach (var sphere in part.Shape.GetAllSpheres())
                {
                    if (sphere.CollisionAttributeIndex == attributeIndex)
                    {
                        spheres++;
                        surfaceProperties.Add(surfaceHashes[sphere.SurfacePropertyIndex]);
                    }
                }

                foreach (var capsule in part.Shape.GetAllCapsules())
                {
                    if (capsule.CollisionAttributeIndex == attributeIndex)
                    {
                        capsules++;
                        surfaceProperties.Add(surfaceHashes[capsule.SurfacePropertyIndex]);
                    }
                }

                foreach (var hull in part.Shape.GetAllHulls())
                {
                    if (hull.CollisionAttributeIndex == attributeIndex)
                    {
                        hulls++;
                        surfaceProperties.Add(surfaceHashes[hull.SurfacePropertyIndex]);
                    }
                }

                foreach (var mesh in part.Shape.GetAllMeshes())
                {
                    if (mesh.CollisionAttributeIndex != attributeIndex)
                    {
                        continue;
                    }

                    meshes++;
                    meshTriangles += mesh.Shape.GetTriangles().Length;

                    // Triangles can each name their own surface property
                    var triangleMaterials = mesh.Shape.Materials;

                    if (triangleMaterials.Length == 0)
                    {
                        surfaceProperties.Add(surfaceHashes[mesh.SurfacePropertyIndex]);
                        continue;
                    }

                    foreach (var material in triangleMaterials)
                    {
                        surfaceProperties.Add(surfaceHashes[material]);
                    }
                }
            }

            var shapes = new List<string>();
            AddShapeCount(shapes, spheres, "sphere", "spheres");
            AddShapeCount(shapes, capsules, "capsule", "capsules");
            AddShapeCount(shapes, hulls, "hull", "hulls");

            AddShapeCount(shapes, meshes, "mesh", "meshes");
            AddShapeCount(shapes, meshTriangles, "mesh triangle", "mesh triangles");

            if (shapes.Count > 0)
            {
                info.AddProperty("Shapes", string.Join(", ", shapes));
            }

            if (surfaceProperties.Count > 0)
            {
                var names = surfaceProperties.Select(StringToken.GetKnownString).Order();
                info.AddProperty("Surface Properties", string.Join(", ", names));
            }
        }

        private static void AddTags(EntityInfoControl info, string name, string[] tags)
        {
            if (tags.Length > 0)
            {
                info.AddProperty(name, string.Join(", ", tags));
            }
        }

        private static void AddShapeCount(List<string> shapes, int count, string singular, string plural)
        {
            if (count == 0)
            {
                return;
            }

            var name = count == 1 ? singular : plural;
            shapes.Add(string.Create(CultureInfo.InvariantCulture, $"{count} {name}"));
        }
    }
}
