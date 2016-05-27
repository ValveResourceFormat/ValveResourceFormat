using System;
using System.Collections.Generic;
using OpenTK;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    internal class MeshObject
    {
        public Resource Resource { get; set; }
        public Matrix4 Transform { get; set; } = Matrix4.Identity;
        public Vector4 TintColor { get; set; } = Vector4.One;
        public List<DrawCall> DrawCalls { get; set; } = new List<DrawCall>();
        public List<string> SkinMaterials { get; set; }
    }
}
