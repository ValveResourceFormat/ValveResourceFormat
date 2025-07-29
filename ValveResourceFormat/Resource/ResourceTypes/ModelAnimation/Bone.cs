#nullable disable

using System.Diagnostics;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    [DebuggerDisplay("{Name} (Index: {Index})")]
    public class Bone
    {
        public int Index { get; }
        public ModelSkeletonBoneFlags Flags { get; }
        public Bone Parent { get; private set; }
        public List<Bone> Children { get; } = [];

        public string Name { get; }

        public Vector3 Position { get; }
        public Quaternion Angle { get; }

        public Matrix4x4 BindPose { get; }
        public Matrix4x4 InverseBindPose { get; }

        public bool IsProceduralCloth => (Flags & ModelSkeletonBoneFlags.ProceduralCloth) == ModelSkeletonBoneFlags.ProceduralCloth;

        public Bone(int index, string name, Vector3 position, Quaternion rotation, ModelSkeletonBoneFlags flags)
        {
            Index = index;
            Name = name;
            Flags = flags;

            Position = position;
            Angle = rotation;

            // Calculate matrices
            BindPose = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);

            if (!Matrix4x4.Invert(BindPose, out var inverseBindPose))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            InverseBindPose = inverseBindPose;
        }

        public void SetParent(Bone parent)
        {
            if (!Children.Contains(parent))
            {
                Parent = parent;
                parent.Children.Add(this);
            }
        }
    }
}
