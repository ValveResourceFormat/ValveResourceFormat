using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class MdlFile
{
    // Largest cosine between the aim of an eye and the forward direction of the eyes attachment when the header gives none
    private const float DefaultMaxEyeDeflection = 0.866f;

    // Offset of the maximum eye deflection in the secondary header
    private const int MaxEyeDeflectionOffset = 12;

    /// <summary>The eyeballs of each sub-model, indexed by body part and sub-model.</summary>
    public StudioEyeball[][][] Eyeballs { get; private set; } = [];

    /// <summary>The cosine of the largest angle the eyes may turn away from the forward direction of the eyes attachment.</summary>
    public float MaxEyeDeflection { get; private set; } = DefaultMaxEyeDeflection;

    private static StudioEyeball[] ReadEyeballs(byte[] data, long modelStart, StudioModel model)
    {
        if (model.NumEyeBalls <= 0)
        {
            return [];
        }

        return LumpReader<StudioEyeball>.ReadLumpAt(data, modelStart + model.EyeBallIndex, model.NumEyeBalls);
    }

    private void ReadMaxEyeDeflection(byte[] data)
    {
        if (Hdr.StudioHdr2Offset <= 0)
        {
            return;
        }

        var offset = (long)Hdr.StudioHdr2Offset + MaxEyeDeflectionOffset;

        if (offset + sizeof(float) > data.Length)
        {
            throw new InvalidDataException("MDL secondary header is out of range.");
        }

        var deflection = BitConverter.ToSingle(data, (int)offset);

        if (deflection != 0f)
        {
            MaxEyeDeflection = deflection;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioEyeball
    {
        public int NameIndex;
        public int Bone;
        public Vector3 Origin;
        public float ZOffset;
        public float Radius;
        public Vector3 Up;
        public Vector3 Forward;
        public int Texture;
        public int _unused1;
        public float IrisScale;
        public int _unused2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public int[] UpperFlexDesc;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public int[] LowerFlexDesc;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] UpperTarget;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] LowerTarget;

        public int UpperLidFlexDesc;
        public int LowerLidFlexDesc;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public int[] _unused;

        public byte NonFacs;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] _unused3;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public int[] _unused4;
    }
}
