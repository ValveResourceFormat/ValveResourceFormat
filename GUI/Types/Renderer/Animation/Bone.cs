using OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Types.Renderer.Animation
{
    internal class Bone
    {
        public Bone Parent { get; private set; }
        public List<Bone> Children { get; }

        public string Name { get; }

        public Matrix4 InverseBindPose { get; }

        public Bone(string name, Matrix4 invBindPose)
        {
            Parent = null;
            Children = new List<Bone>();

            Name = name;

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
