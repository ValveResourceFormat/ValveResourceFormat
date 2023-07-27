using System.Numerics;
using System.Runtime.InteropServices;

namespace GUI.Types.Renderer.UniformBuffers
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewConstants
    {
        public Matrix4x4 ViewToProjection;
        public Matrix4x4 LightPosition;
        public Vector4 LightColor;
        public Vector3 CameraPosition;
        public float Time;
    }
}
