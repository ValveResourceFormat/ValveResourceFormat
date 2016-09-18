using System.Collections.Generic;
using OpenTK;

namespace GUI.Types.Renderer.Animation
{
    internal class Bone
    {
        public Bone Parent { get; private set; }
        public List<Bone> Children { get; }

        public string Name { get; }
        public int Index { get; }

        public Vector3 Position { get; }
        public Quaternion Angle { get; }

        public Matrix4 BindPose { get; }
        public Matrix4 InverseBindPose { get; }

        public Bone(string name, int index, Vector3 position, Quaternion rotation)
        {
            Parent = null;
            Children = new List<Bone>();

            Name = name;
            Index = index;

            Position = position;
            Angle = rotation;

            // Calculate matrices
            var bindPose = Matrix4.CreateFromQuaternion(rotation) * Matrix4.CreateTranslation(position);
            var invBindPose = bindPose.Inverted();

            BindPose = bindPose;
            InverseBindPose = invBindPose;
        }

        public void AddChild(Bone child)
        {
            Children.Add(child);
        }

        public void SetParent(Bone parent)
        {
            Parent = parent;
        }
    }
}
