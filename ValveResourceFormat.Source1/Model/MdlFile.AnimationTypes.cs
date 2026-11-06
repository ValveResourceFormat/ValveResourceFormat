using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class MdlFile
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct AnimDesc
    {
        public int BasePointer;
        public int NameIndex;
        public float Fps;
        public int Flags;
        public int NumFrames;
        public int NumMovements;
        public int MovementIndex;
        public int IkRuleZeroFrameIndex;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public int[] _unused1;

        public int AnimBlock;
        public int AnimIndex;
        public int NumIkRules;
        public int IkRuleIndex;
        public int AnimBlockIkRuleIndex;
        public int NumLocalHierarchy;
        public int LocalHierarchyIndex;
        public int SectionIndex;
        public int SectionFrames;
        public short ZeroFrameSpan;
        public short ZeroFrameCount;
        public int ZeroFrameIndex;
        public float ZeroFrameStallTime;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SeqDesc
    {
        public int BasePointer;
        public int LabelIndex;
        public int ActivityNameIndex;
        public int Flags;
        public int Activity;
        public int ActivityWeight;
        public int NumEvents;
        public int EventIndex;
        public Vector3 BoundingBoxMin;
        public Vector3 BoundingBoxMax;
        public int NumBlends;
        public int AnimIndexIndex;
        public int MovementIndex;
        public int GroupSize0;
        public int GroupSize1;
        public int ParamIndex0;
        public int ParamIndex1;
        public float ParamStart0;
        public float ParamStart1;
        public float ParamEnd0;
        public float ParamEnd1;
        public int ParamParent;
        public float FadeInTime;
        public float FadeOutTime;
        public int LocalEntryNode;
        public int LocalExitNode;
        public int NodeFlags;
        public float EntryPhase;
        public float ExitPhase;
        public float LastFrame;
        public int NextSeq;
        public int Pose;
        public int NumIkRules;
        public int NumAutoLayers;
        public int AutoLayerIndex;
        public int WeightListIndex;
        public int PoseKeyIndex;
        public int NumIkLocks;
        public int IkLockIndex;
        public int KeyValueIndex;
        public int KeyValueSize;
        public int CyclePoseIndex;
        public int ActivityModifierIndex;
        public int NumActivityModifiers;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public int[] _unused;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct AutoLayer
    {
        public short Sequence;
        public short Pose;
        public int Flags;
        public float Start;
        public float Peak;
        public float Tail;
        public float End;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SeqEvent
    {
        public float Cycle;
        public int Event;
        public int Type;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Options;

        public int NameIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct AnimMovement
    {
        public int EndFrame;
        public int MotionFlags;
        public float V0;
        public float V1;
        public float Angle;
        public Vector3 Vector;
        public Vector3 Position;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct LocalHierarchy
    {
        public int Bone;
        public int NewParent;
        public float Start;
        public float Peak;
        public float Tail;
        public float End;
        public int StartFrame;
        public int LocalAnimIndex;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public int[] _unused;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioIkChain
    {
        public int NameIndex;
        public int LinkType;
        public int NumLinks;
        public int LinkIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioIkLink
    {
        public int Bone;
        public Vector3 KneeDirection;
        public Vector3 _unused;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioIkRule
    {
        public int Index;
        public int Type;
        public int Chain;
        public int Bone;
        public int Slot;
        public float Height;
        public float Radius;
        public float Floor;
        public Vector3 Position;
        public Quaternion Rotation;
        public int CompressedIkErrorIndex;
        public int _unused2;
        public int StartFrame;
        public int IkErrorIndex;
        public float Start;
        public float Peak;
        public float Tail;
        public float End;
        public float _unused3;
        public float Contact;
        public float Drop;
        public float Top;
        public int _unused6;
        public int _unused7;
        public int _unused8;
        public int AttachmentIndex;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public int[] _unused;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct PoseParamDesc
    {
        public int NameIndex;
        public int Flags;
        public float Start;
        public float End;
        public float Loop;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ModelGroup
    {
        public int LabelIndex;
        public int NameIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct AnimBlock
    {
        public int DataStart;
        public int DataEnd;
    }
}
