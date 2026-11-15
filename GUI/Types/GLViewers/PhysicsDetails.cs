using System.Globalization;
using System.Linq;
using GUI.Forms;
using ValveResourceFormat.Renderer.Editor.Collision;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

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
                info.AddProperty(Locale.Physics_File, node.Name, new LinkedFile(node.Name));
            }

            info.AddProperty(Locale.Physics_Collision, string.Join(", ", node.CollisionLabels));

            AddGroup(info, node.Physics, node.CollisionAttributeIndex);
        }

        public static void AddEntityDetails(EntityInfoControl info, BaseModelEntity entity, PhysAggregateData physics)
        {
            info.AddProperty(Locale.Physics_SolidType, entity.Solid.ToString());

            var mass = 0f;

            foreach (var part in physics.Parts)
            {
                mass += part.Mass;
            }

            if (mass > 0f)
            {
                info.AddProperty(Locale.Physics_Mass, mass.ToString("G6", CultureInfo.InvariantCulture));
            }

            if (physics.Joints.Length > 0)
            {
                info.AddProperty(Locale.Physics_Joints, physics.Joints.Length.ToString(CultureInfo.InvariantCulture));
            }

            for (var i = 0; i < physics.CollisionAttributes.Count; i++)
            {
                AddGroup(info, physics, i);
            }
        }

        private static void AddGroup(EntityInfoControl info, PhysAggregateData physics, int attributeIndex)
        {
            var attributes = physics.CollisionAttributes[attributeIndex];

            info.AddProperty(Locale.Physics_CollisionGroup, attributes.GetStringProperty("m_CollisionGroupString") ?? string.Empty);
            AddTags(info, Locale.Physics_InteractAs, PhysAggregateData.GetInteractAsTags(attributes));
            AddTags(info, Locale.Physics_InteractWith, attributes.GetArray<string>("m_InteractWithStrings") ?? []);
            AddTags(info, Locale.Physics_InteractExclude, attributes.GetArray<string>("m_InteractExcludeStrings") ?? []);

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
            if (spheres > 0)
            {
                shapes.Add(Locale.Physics_Spheres(spheres));
            }

            if (capsules > 0)
            {
                shapes.Add(Locale.Physics_Capsules(capsules));
            }

            if (hulls > 0)
            {
                shapes.Add(Locale.Physics_Hulls(hulls));
            }

            if (meshes > 0)
            {
                shapes.Add(Locale.Physics_Meshes(meshes));
            }

            if (meshTriangles > 0)
            {
                shapes.Add(Locale.Physics_MeshTriangles(meshTriangles));
            }

            if (shapes.Count > 0)
            {
                info.AddProperty(Locale.Physics_Shapes, string.Join(", ", shapes));
            }

            if (surfaceProperties.Count > 0)
            {
                var names = surfaceProperties.Select(StringToken.GetKnownString).Order();
                info.AddProperty(Locale.Physics_SurfaceProperties, string.Join(", ", names));
            }
        }

        private static void AddTags(EntityInfoControl info, string name, string[] tags)
        {
            if (tags.Length > 0)
            {
                info.AddProperty(name, string.Join(", ", tags));
            }
        }
    }
}
