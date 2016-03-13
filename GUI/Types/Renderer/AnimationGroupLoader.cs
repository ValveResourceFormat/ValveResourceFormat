using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace GUI.Types.Renderer
{
    class AnimationGroupLoader
    {
        private NTRO data;
        private AnimationGroup animationGroup;

        public struct AnimationGroup {
            public string name;
            public uint flags;
            public DecodeKey decodeKey;
        }

        public struct DecodeKey
        {
            public string name;
            public AnimResourceBone[] boneArray;
            public int channelElements;
        }

        public struct AnimResourceBone
        {
            public string name;
            public int parent;
            public OpenTK.Vector3 pos;
            public OpenTK.Quaternion quat;
            public OpenTK.Quaternion qAlignment;
            public int flags;
        }

        public AnimationGroupLoader(Resource resource, string filename)
        {
            data = (NTRO) resource.Blocks[BlockType.DATA];
            //Console.WriteLine(data);
            loadAnimationGroup();
        }

        private void loadAnimationGroup()
        {
            animationGroup = new AnimationGroup();
            animationGroup.name = ((NTROValue<string>)data.Output["m_name"]).Value;

            //ExternalReference m_localHAnimArray[]
            //ExternalReference m_includedGroupArray[]
            //ExternalReference m_directHSeqGroup

            animationGroup.decodeKey = new DecodeKey();

            var decodeKey = (NTROValue<NTROStruct>)data.Output["m_decodeKey"];
            animationGroup.decodeKey.name = ((NTROValue<string>)decodeKey.Value["m_name"]).Value;

            var boneArray = (NTROArray)decodeKey.Value["m_boneArray"];

            animationGroup.decodeKey.boneArray = new AnimResourceBone[boneArray.Count];

            for (int i = 0; i < boneArray.Count; i++)
            {
                var subStruct = ((NTROValue<NTROStruct>)boneArray[i]).Value;

                var bone = new AnimResourceBone();
                bone.name = ((NTROValue<string>)subStruct["m_name"]).Value;
                bone.parent = ((NTROValue<int>)subStruct["m_parent"]).Value;
                bone.pos = new OpenTK.Vector3(((NTROValue<Vector3>)subStruct["m_pos"]).Value.field0, ((NTROValue<Vector3>)subStruct["m_pos"]).Value.field1, ((NTROValue<Vector3>)subStruct["m_pos"]).Value.field2);
                bone.quat = new OpenTK.Quaternion(((NTROValue<Vector4>)subStruct["m_quat"]).Value.field0, ((NTROValue<Vector4>)subStruct["m_quat"]).Value.field1, ((NTROValue<Vector4>)subStruct["m_quat"]).Value.field2, ((NTROValue<Vector4>)subStruct["m_quat"]).Value.field3);
                bone.qAlignment = new OpenTK.Quaternion(((NTROValue<Vector4>)subStruct["m_qAlignment"]).Value.field0, ((NTROValue<Vector4>)subStruct["m_qAlignment"]).Value.field1, ((NTROValue<Vector4>)subStruct["m_qAlignment"]).Value.field2, ((NTROValue<Vector4>)subStruct["m_qAlignment"]).Value.field3);
                bone.flags = ((NTROValue<int>)subStruct["m_flags"]).Value; 

                animationGroup.decodeKey.boneArray[i] = bone;
            }

            //Struct m_userArray[]
            //CResourceString m_morphArray[]
            //Struct m_IKChainArray[]

            animationGroup.decodeKey.channelElements = ((NTROValue<int>)decodeKey.Value["m_nChannelElements"]).Value;

            //Struct m_dataChannelArray[]
            //  AnimResourceDataChannel_t
            //      CResourceString m_szChannelClass
            //      CResourceString m_szVariableName
            //      int32 m_nFlags
            //      int32 m_nType
            //      CResourceString m_szGrouping
            //      CResourceString m_szDescription
            //      CResourceString m_szElementNameArray[]
            //      int32 m_nElementIndexArray[]
            //      uint32 m_nElementMaskArray[]
        }
    }
}
