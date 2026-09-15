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

    /// <summary>
    /// Writes the collision property a physics shape's collision attributes compile from.
    /// </summary>
    private void AddCollisionProperty(KVObject node, int collisionAttributeIndex)
    {
        if (collisionAttributeIndex >= 0 && collisionAttributeIndex < PhysicsCollisionProperties.Length
            && PhysicsCollisionProperties[collisionAttributeIndex] is { } collisionProperty)
        {
            node.Add("collision_prop", collisionProperty);
        }
    }

    private string?[] GetCollisionPropertyNames(IReadOnlyList<KVObject> collisionAttributes)
    {
        var names = new string?[collisionAttributes.Count];
        using var stream = fileLoader?.GetFileStream("scripts/collision_properties.txt");

        if (stream == null)
        {
            return names;
        }

        var collisionProperties = KVDocumentExtensions.ParseKV3(stream).Root.GetArray("collision_properties");

        for (var i = 0; i < collisionAttributes.Count; i++)
        {
            names[i] = FindCollisionPropertyName(collisionProperties, collisionAttributes[i]);
        }

        return names;
    }

    /// <summary>
    /// Finds the first <c>scripts/collision_properties.txt</c> entry whose collision group and interaction layers
    /// compile to <paramref name="collisionAttributes"/>, or <see langword="null"/> when that is the default entry
    /// or no entry does.
    /// </summary>
    private static string? FindCollisionPropertyName(IReadOnlyList<KVObject> collisionProperties, KVObject collisionAttributes)
    {
        foreach (var collisionProperty in collisionProperties)
        {
            if (string.Equals(collisionProperty.GetStringProperty("collision_group"),
                    collisionAttributes.GetStringProperty("m_CollisionGroupString"), StringComparison.OrdinalIgnoreCase)
                && HaveSameLayers(collisionProperty.GetArray<string>("interact_as"), PhysAggregateData.GetInteractAsTags(collisionAttributes))
                && HaveSameLayers(collisionProperty.GetArray<string>("interact_with"), collisionAttributes.GetArray<string>("m_InteractWithStrings"))
                && HaveSameLayers(collisionProperty.GetArray<string>("interact_exclude"), collisionAttributes.GetArray<string>("m_InteractExcludeStrings")))
            {
                var name = collisionProperty.GetStringProperty("name");
                return name == "default" ? null : name;
            }
        }

        return null;
    }

    private static bool HaveSameLayers(string[]? authored, string[]? compiled)
        => new HashSet<string>((authored ?? []).Where(layer => layer.Length > 0), StringComparer.OrdinalIgnoreCase)
            .SetEquals((compiled ?? []).Where(layer => layer.Length > 0));

    private void AddPhysicsShapeFileNodes(ModelDocLists lists)
    {
        if (Type != ModelExtractType.Map_PhysicsToRenderMesh || (PhysHullsToExtract.Count == 0 && PhysMeshesToExtract.Count == 0))
        {
            return;
        }

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

        foreach (var (physHull, fileName, parentBone, _) in PhysHullsToExtract)
        {
            AddPhysMeshNode(lists, physHull, fileName, parentBone);
        }

        foreach (var (physMesh, fileName, parentBone, _) in PhysMeshesToExtract)
        {
            AddPhysMeshNode(lists, physMesh, fileName, parentBone);
        }
    }

    /// <summary>
    /// Writes each physics part's shapes together, in part order, followed by the joints between the parts.
    /// </summary>
    private void AddPhysicsBodyNodes(ModelDocLists lists)
    {
        if (physAggregateData is not null)
        {
            var jointNodes = BuildPhysicsJointNodes(physAggregateData);

            AddPhysicsBodyMarkup(lists, physAggregateData);

            var writesShapeFiles = Type != ModelExtractType.Map_PhysicsToRenderMesh;
            var hullIndex = 0;
            var meshIndex = 0;

            for (var i = 0; i < physAggregateData.Parts.Length; i++)
            {
                var physicsPart = physAggregateData.Parts[i];
                var shape = physicsPart.Shape;
                var parentBone = physAggregateData.GetParentBoneName(i);

                foreach (var sphere in shape.Spheres)
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
                    AddCollisionProperty(physicsShapeSphere, sphere.CollisionAttributeIndex);

                    lists.PhysicsShapes.Add(physicsShapeSphere);
                }

                foreach (var capsule in shape.Capsules)
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
                    AddCollisionProperty(physicsShapeCapsule, capsule.CollisionAttributeIndex);

                    lists.PhysicsShapes.Add(physicsShapeCapsule);
                }

                int hullCount = Math.Min(shape.Hulls.Length, PhysHullsToExtract.Count - hullIndex);
                int meshCount = Math.Min(shape.Meshes.Length, PhysMeshesToExtract.Count - meshIndex);

                if (writesShapeFiles)
                {
                    for (var j = hullIndex; j < hullIndex + hullCount; j++)
                    {
                        var (physHull, fileName, hullBone, _) = PhysHullsToExtract[j];
                        AddPhysMeshNode(lists, physHull, fileName, hullBone);
                    }

                    for (var j = meshIndex; j < meshIndex + meshCount; j++)
                    {
                        var (physMesh, fileName, meshBone, _) = PhysMeshesToExtract[j];
                        AddPhysMeshNode(lists, physMesh, fileName, meshBone);
                    }
                }

                hullIndex += hullCount;
                meshIndex += meshCount;

                if (parentBone.Length > 0 && shape.Spheres.Length == 0 && shape.Capsules.Length == 0 && shape.Hulls.Length == 0 && shape.Meshes.Length == 0)
                {
                    HashSet<string> collisionTags = physicsPart.CollisionAttributeIndex < PhysicsCollisionTags.Length
                        ? PhysicsCollisionTags[physicsPart.CollisionAttributeIndex]
                        : [];

                    // A zero radius sphere compiles to no shape but keeps the body and the joints on it.
                    var shapelessBodySphere = MakeNode(
                        "PhysicsShapeSphere",
                        ("parent_bone", parentBone),
                        ("surface_prop", PhysicsSurfaceNames[0]),
                        ("collision_tags", string.Join(" ", collisionTags)),
                        ("radius", 0f),
                        ("center", ToKVArray(Vector3.Zero)),
                        ("name", string.Empty)
                    );

                    AddCollisionProperty(shapelessBodySphere, physicsPart.CollisionAttributeIndex);
                    lists.PhysicsShapes.Add(shapelessBodySphere);
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
        AddCollisionProperty(physicsShapeFile, shapeDesc.CollisionAttributeIndex);

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
