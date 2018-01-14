/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2018 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 *
 * Code from https://github.com/FNA-XNA/FNA/blob/1f330620fb09ce3ce59e444a1731bb7903a09d20/src/Graphics/PackedVector/HalfTypeHelper.cs
 */
using System.Runtime.InteropServices;

namespace ValveResourceFormat
{
    internal static class HalfTypeHelper
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct HalfTypeHackCast
        {
            [FieldOffset(0)]
            public float Float;

            [FieldOffset(0)]
            public uint UnsignedInteger;
        }

        internal static float Convert(ushort value)
        {
            uint rst;
            uint mantissa = (uint)(value & 1023);
            uint exp = 0xfffffff2;

            if ((value & -33792) == 0)
            {
                if (mantissa != 0)
                {
                    while ((mantissa & 1024) == 0)
                    {
                        exp--;
                        mantissa = mantissa << 1;
                    }

                    mantissa &= 0xfffffbff;
                    rst = (((uint)value & 0x8000) << 16) | ((exp + 127) << 23) | (mantissa << 13);
                }
                else
                {
                    rst = (uint)((value & 0x8000) << 16);
                }
            }
            else
            {
                rst = (((uint)value & 0x8000) << 16) | (((((uint)value >> 10) & 0x1f) - 15 + 127) << 23) | (mantissa << 13);
            }

            var uif = new HalfTypeHackCast
            {
                UnsignedInteger = rst,
            };
            return uif.Float;
        }
    }
}
