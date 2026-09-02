using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData
{
    /// <summary>
    /// One entry of a model's authored bone constraint list: a rule that drives a bone, or a morph,
    /// from the pose of other bones.
    /// </summary>
    /// <param name="ClassName">The compiled constraint class, e.g. <c>CTwistConstraint</c>.</param>
    /// <param name="Data">The compiled object the constraint's own fields are read from.</param>
    public readonly record struct BoneConstraint(string ClassName, KVObject Data)
    {
        /// <summary>
        /// Reads a model's constraint list in compiled order. The compiler writes a null entry for a
        /// constraint it rejected, and those are left out.
        /// </summary>
        internal static BoneConstraint[] ReadList(KVObject keyValues)
        {
            if (!keyValues.ContainsKey("BoneConstraintList"))
            {
                return [];
            }

            var constraints = new List<BoneConstraint>();

            foreach (var data in keyValues.GetArray("BoneConstraintList"))
            {
                if (data == null)
                {
                    continue;
                }

                constraints.Add(new BoneConstraint(data.GetStringProperty("_class"), data));
            }

            return [.. constraints];
        }
    }
}
