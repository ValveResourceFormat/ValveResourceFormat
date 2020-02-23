using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Utils
{
    internal static class VectorExtensions
    {
        public static OpenTK.Vector3 ToOpenTK(this System.Numerics.Vector3 vec)
        {
            return new OpenTK.Vector3(vec.X, vec.Y, vec.Z);
        }

        public static OpenTK.Vector4 ToOpenTK(this System.Numerics.Vector4 vec)
        {
            return new OpenTK.Vector4(vec.X, vec.Y, vec.Z, vec.W);
        }
    }
}
