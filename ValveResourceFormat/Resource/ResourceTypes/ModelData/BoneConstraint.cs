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
        /// Reads a model's constraint list in compiled order. A constraint the compiler rejected is
        /// written as a null entry, or as an entry that keeps its slot but carries no class, and
        /// neither is read back.
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

                var className = data.GetStringProperty("_class");
                if (string.IsNullOrEmpty(className))
                {
                    continue;
                }

                constraints.Add(new BoneConstraint(className, data));
            }

            return [.. constraints];
        }
    }
}
