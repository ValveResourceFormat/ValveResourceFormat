using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Physics;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// Converts the collision of a model to Source 2 physics with a part for each solid, which follows the bone the solid
/// belongs to, and a joint for each constraint between the solids of a ragdoll.
/// </summary>
internal static class Source1ModelPhysicsConverter
{
    private const int TwistAxis = 0;

    // How far a child solid can turn about an axis of its bone, in radians
    private readonly record struct AxisLimit(float Min, float Max)
    {
        public float Range => Max - Min;
        public float Middle => (Min + Max) / 2f;
    }

    /// <summary>Gets whether a model has collision to convert.</summary>
    public static bool HasCollision(Source1Model source) => source.Phy is { Solids.Length: > 0 } && source.Bones.Length > 0;

    /// <summary>Converts the collision of a model.</summary>
    /// <param name="source">The model.</param>
    /// <returns>The data of the physics aggregate, or <see langword="null"/> when the model has no collision.</returns>
    public static KVObject? Convert(Source1Model source)
    {
        if (!HasCollision(source))
        {
            return null;
        }

        var phy = source.Phy!;
        var bindToRest = Source1ModelConverter.GetBindToRest(source);
        var sections = Source1WorldPhysicsConverter.ParseKeyValues(phy.KeyValuesText);
        var materialNames = Source1WorldPhysicsConverter.GetMaterialNames(sections);
        var solidSections = Source1WorldPhysicsConverter.GetSolidSections(sections);
        var builder = new PhysicsAggregateBuilder();

        var isRagdoll = IsRagdoll(source, sections, solidSections);
        var partIndices = new Dictionary<int, int>();
        var partBones = new Dictionary<int, int>();

        foreach (var solid in phy.Solids)
        {
            solidSections.TryGetValue(solid.Index, out var section);

            var boneIndex = FindBone(source, solid.Index, section);
            var bone = source.Bones[boneIndex];
            var mass = GetMass(section);

            var boneToModel = Source1ModelConverter.GetBoneToModel(source, boneIndex, bindToRest);

            partIndices[solid.Index] = builder.BeginPart(bone.Name, boneToModel, mass);
            partBones[solid.Index] = boneIndex;

            if (isRagdoll)
            {
                var source1ToFrame = Source1ModelConverter.GetSource1ToFrame(source, boneIndex);
                Source1WorldPhysicsConverter.AddSolid(builder, Transform(solid, source1ToFrame), section, materialNames);
                continue;
            }

            // The points are in the space of the vertices of the model, and each part keeps them in the space of its bone
            var modelToBone = Matrix4x4.Invert(boneToModel, out var inverse) ? inverse : Matrix4x4.Identity;
            var toBone = bindToRest * modelToBone;

            Source1WorldPhysicsConverter.AddSolid(builder, Transform(solid, toBone), section, materialNames);
        }

        if (isRagdoll)
        {
            AddJoints(builder, source, sections, partIndices, partBones);
        }

        return builder.IsEmpty ? null : builder.BuildData();
    }

    // A ragdoll constraint limits the turn of a child solid about the axes of its bone, in degrees, measured from the bind
    // pose with positive turns following the right hand rule. One free axis makes a hinge. Otherwise the axis along the
    // bone twists and the free axis with the larger range sets a cone around the middle of that range; the range of the
    // other axis has no counterpart in a cone. Source 2 joints twist about the Z axis of their frames.
    private static void AddJoints(
        PhysicsAggregateBuilder builder,
        Source1Model source,
        List<Source1WorldPhysicsConverter.Section> sections,
        Dictionary<int, int> partIndices,
        Dictionary<int, int> partBones)
    {
        foreach (var section in sections)
        {
            if (section.Name != "ragdollconstraint")
            {
                continue;
            }

            if (!TryGetInt(section, "parent", out var parent) || !TryGetInt(section, "child", out var child) || parent == child)
            {
                continue;
            }

            if (!partIndices.TryGetValue(parent, out var parentPart) || !partIndices.TryGetValue(child, out var childPart))
            {
                continue;
            }

            AxisLimit[] limits = [GetLimit(section, "x"), GetLimit(section, "y"), GetLimit(section, "z")];
            var freeAxes = 0;
            var lastFreeAxis = TwistAxis;

            for (var axis = 0; axis < limits.Length; axis++)
            {
                if (limits[axis].Min != limits[axis].Max)
                {
                    freeAxes++;
                    lastFreeAxis = axis;
                }
            }

            var jointType = JointType.Conical;
            var jointAxis = TwistAxis;
            var coneCenter = Matrix4x4.Identity;
            var swingLimit = 0f;

            if (freeAxes == 0)
            {
                jointType = JointType.Weld;
            }
            else if (freeAxes == 1)
            {
                jointType = JointType.Revolute;
                jointAxis = lastFreeAxis;
            }
            else
            {
                var swingAxis = limits[1].Range > limits[2].Range ? 1 : 2;
                var swingDirection = swingAxis == 1 ? Vector3.UnitY : Vector3.UnitZ;
                coneCenter = Matrix4x4.CreateFromAxisAngle(swingDirection, limits[swingAxis].Middle);
                swingLimit = limits[swingAxis].Range / 2f;
            }

            var jointToBone = GetJointToBone(jointAxis);
            var childBone = partBones[child];
            var jointToChild = jointToBone * Source1ModelConverter.GetSource1ToFrame(source, childBone);
            var jointToParent = jointToBone * coneCenter * GetBoneToParent(source, childBone, partBones[parent]);
            var twist = limits[jointAxis];

            builder.AddJoint(jointType, parentPart, childPart, jointToParent, jointToChild, twist.Min, twist.Max, swingLimit);
        }
    }

    // Takes the frame of a joint, which twists about its Z axis, to the space of a Source 1 bone turning about the given axis
    private static Matrix4x4 GetJointToBone(int axis) => axis switch
    {
        0 => new Matrix4x4(0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f),
        1 => new Matrix4x4(0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f),
        _ => Matrix4x4.Identity,
    };

    // Takes the space of the Source 1 child bone to the one of the converted parent bone in the bind pose
    private static Matrix4x4 GetBoneToParent(Source1Model source, int childBone, int parentBone)
    {
        var childToModel = Matrix4x4.Invert(source.Bones[childBone].PoseToBone, out var inverse) ? inverse : Matrix4x4.Identity;

        return childToModel * Source1ModelConverter.GetPoseToBone(source, parentBone);
    }

    private static AxisLimit GetLimit(Source1WorldPhysicsConverter.Section section, string axis)
    {
        var min = float.DegreesToRadians(GetFloat(section, axis + "min"));
        var max = float.DegreesToRadians(GetFloat(section, axis + "max"));

        return new AxisLimit(min, max);
    }

    private static float GetFloat(Source1WorldPhysicsConverter.Section section, string key)
    {
        if (section.Values.TryGetValue(key, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return 0f;
    }

    private static bool TryGetInt(Source1WorldPhysicsConverter.Section section, string key, out int value)
    {
        value = 0;

        return section.Values.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    // The solids of a ragdoll are bodies with their points in the space of their bone, linked by constraints, or named
    // after the bones they hang from. A prop has its points in the space of the model.
    private static bool IsRagdoll(Source1Model source, List<Source1WorldPhysicsConverter.Section> sections, Dictionary<int, Source1WorldPhysicsConverter.Section> solidSections)
    {
        foreach (var section in sections)
        {
            if (section.Name == "ragdollconstraint" || section.Values.ContainsKey("parent"))
            {
                return true;
            }
        }

        if (solidSections.Count < 2)
        {
            return false;
        }

        foreach (var section in solidSections.Values)
        {
            var name = section.Values.GetValueOrDefault("name");

            if (!HasBone(source, name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasBone(Source1Model source, string? name) => FindBoneByName(source, name) >= 0;

    private static int FindBoneByName(Source1Model source, string? name)
    {
        for (var i = 0; i < source.Bones.Length; i++)
        {
            if (string.Equals(source.Bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // The solids of a ragdoll are named after their bones, and name the solid they hang from. A solid of a prop has
    // a name of its own, so it belongs to the bone the model gives it, or else to the root.
    private static int FindBone(Source1Model source, int solidIndex, Source1WorldPhysicsConverter.Section? section)
    {
        var byName = FindBoneByName(source, section?.Values.GetValueOrDefault("name"));

        if (byName >= 0)
        {
            return byName;
        }

        for (var i = 0; i < source.Bones.Length; i++)
        {
            if (source.Bones[i].PhysicsBone == solidIndex)
            {
                return i;
            }
        }

        return 0;
    }

    private static float GetMass(Source1WorldPhysicsConverter.Section? section)
    {
        if (section != null
            && section.Values.TryGetValue("mass", out var text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var mass))
        {
            return mass;
        }

        return 0f;
    }

    private static PhyFile.Solid Transform(PhyFile.Solid solid, Matrix4x4 transform)
    {
        var convexes = new List<Vector3[]>(solid.Convexes.Count);

        foreach (var points in solid.Convexes)
        {
            var transformed = new Vector3[points.Length];

            for (var i = 0; i < points.Length; i++)
            {
                transformed[i] = Vector3.Transform(points[i], transform);
            }

            convexes.Add(transformed);
        }

        return new PhyFile.Solid
        {
            Index = solid.Index,
            MassCenter = Vector3.Transform(solid.MassCenter, transform),
            RawInertia = solid.RawInertia,
            Convexes = convexes,
            ConvexTriangles = solid.ConvexTriangles,
            ConvexMaterials = solid.ConvexMaterials,
        };
    }
}
