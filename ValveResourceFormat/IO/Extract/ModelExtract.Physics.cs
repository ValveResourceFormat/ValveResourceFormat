using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for the collision model: the shapes each physics body carries and the
/// physics shape files.
/// </summary>
partial class ModelExtract
{
    /// <summary>
    /// Writes the hit group a physics shape belongs to, skipping the invalid placeholder.
    /// </summary>
    static void AddHitGroup<TShape>(KVObject node, ShapeDescriptor<TShape> shape) where TShape : struct
    {
        if (!string.IsNullOrEmpty(shape.HitGroupName) && shape.HitGroupName != "HITGROUP_INVALID")
        {
            node.Add("hitgroupname", shape.HitGroupName);
        }
    }

    private void AddPhysicsShapeFileNodes(ModelDocLists lists)
    {
        if (PhysHullsToExtract.Count > 0 || PhysMeshesToExtract.Count > 0)
        {
            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                if (PhysicsToRenderMaterialNameProvider is null)
                {
                    RemapMaterials(lists, globalReplace: true);
                }
                else
                {
                    var remapTable = SurfaceTagCombos.ToDictionary(
                        combo => combo.StringMaterial,
                        combo => PhysicsToRenderMaterialNameProvider(combo)
                    );
                    RemapMaterials(lists, remapTable, globalReplace: false);
                }
            }

            foreach (var (physHull, fileName, parentBone, _) in PhysHullsToExtract)
            {
                AddPhysMeshNode(lists, physHull, fileName, parentBone);
            }

            foreach (var (physMesh, fileName, parentBone, _) in PhysMeshesToExtract)
            {
                AddPhysMeshNode(lists, physMesh, fileName, parentBone);
            }
        }
    }

    private void AddPhysicsBodyNodes(ModelDocLists lists)
    {
        if (physAggregateData is not null)
        {
            var jointNodes = BuildPhysicsJointNodes(physAggregateData);

            AddPhysicsBodyMarkup(lists, physAggregateData);

            for (var i = 0; i < physAggregateData.Parts.Length; i++)
            {
                var physicsPart = physAggregateData.Parts[i];
                var parentBone = physAggregateData.GetParentBoneName(i);

                foreach (var sphere in physicsPart.Shape.Spheres)
                {
                    var physicsShapeSphere = MakeNode(
                        "PhysicsShapeSphere",
                        ("parent_bone", parentBone),
                        ("surface_prop", PhysicsSurfaceNames[sphere.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[sphere.CollisionAttributeIndex])),
                        ("radius", sphere.Shape.Radius),
                        ("center", ToKVArray(sphere.Shape.Center)),
                        ("name", sphere.UserFriendlyName ?? string.Empty)
                    );

                    AddHitGroup(physicsShapeSphere, sphere);

                    lists.PhysicsShapes.Add(physicsShapeSphere);
                }

                foreach (var capsule in physicsPart.Shape.Capsules)
                {
                    var physicsShapeCapsule = MakeNode(
                        "PhysicsShapeCapsule",
                        ("parent_bone", parentBone),
                        ("surface_prop", PhysicsSurfaceNames[capsule.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[capsule.CollisionAttributeIndex])),
                        ("radius", capsule.Shape.Radius),
                        ("point0", ToKVArray(capsule.Shape.Center[0])),
                        ("point1", ToKVArray(capsule.Shape.Center[1])),
                        ("name", capsule.UserFriendlyName ?? string.Empty)
                    );

                    AddHitGroup(physicsShapeCapsule, capsule);

                    lists.PhysicsShapes.Add(physicsShapeCapsule);
                }
            }

            foreach (var jointNode in jointNodes)
            {
                lists.PhysicsJoints.Add(jointNode);
            }
        }
    }

    private void AddPhysMeshNode<TShape>(ModelDocLists lists, ShapeDescriptor<TShape> shapeDesc, string fileName, string parentBone)
        where TShape : struct
    {
        var surfacePropName = PhysicsSurfaceNames[shapeDesc.SurfacePropertyIndex];
        var collisionTags = PhysicsCollisionTags[shapeDesc.CollisionAttributeIndex];

        if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
        {
            lists.RenderMeshes.Add(MakeNode("RenderMeshFile", ("filename", fileName)));
            return;
        }

        var className = shapeDesc switch
        {
            HullDescriptor => "PhysicsHullFile",
            MeshDescriptor => "PhysicsMeshFile",
            _ => throw new NotImplementedException()
        };

        var shapeName = shapeDesc.UserFriendlyName ?? Path.GetFileNameWithoutExtension(fileName);

        // TODO: per faceSet surface_prop
        var physicsShapeFile = MakeNode(
            className,
            ("filename", fileName),
            ("parent_bone", parentBone),
            ("surface_prop", surfacePropName),
            ("collision_tags", string.Join(" ", collisionTags)),
            ("name", shapeName)
        );

        AddHitGroup(physicsShapeFile, shapeDesc);

        lists.PhysicsShapes.Add(physicsShapeFile);
    }

    private static void RemapMaterials(ModelDocLists lists,
        IReadOnlyDictionary<string, string>? remapTable = null,
        bool globalReplace = false,
        string globalDefault = "materials/tools/toolsnodraw.vmat")
    {
        var remaps = KVObject.Array();
        lists.MaterialGroups.Add(
            MakeNode(
                "DefaultMaterialGroup",
                ("remaps", remaps),
                ("use_global_default", globalReplace),
                ("global_default_material", globalDefault)
            )
        );

        if (globalReplace || remapTable == null)
        {
            return;
        }

        foreach (var (from, to) in remapTable)
        {
            var remap = KVObject.Collection();
            remap.Add("from", from);
            remap.Add("to", to);
            remaps.Add(remap);
        }
    }
}
