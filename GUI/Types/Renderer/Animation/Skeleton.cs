using OpenTK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.KeyValues;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer.Animation
{
    internal class Skeleton
    {
        public Dictionary<string, Bone> Bones;

        // Armature constructor
        public Skeleton(Resource mesh)
        {
            Bones = new Dictionary<string, Bone>();

            var meshData = (BinaryKV3)mesh.Blocks[BlockType.DATA];

            // Check if there is any skeleton data present at all
            if (!meshData.Data.Properties.ContainsKey("m_skeleton"))
            {
                Console.WriteLine("No skeleton data found.");
            }

            // Construct the armature from the skeleton KV
            ConstructFromKV((KVObject)meshData.Data.Properties["m_skeleton"].Value);
        }

        // Construct the Armature object from mesh skeleton KV data.
        public void ConstructFromKV(KVObject data)
        {
            var boneList = ((KVObject)data.Properties["m_bones"].Value).Properties;
            // Loop over all bones in skeleton
            foreach (var li in boneList.Values)
            {
                var boneKV = (KVObject)li.Value;

                //Cast names
                var boneName = (string)boneKV.Properties["m_boneName"].Value;
                var parentName = (string)boneKV.Properties["m_parentName"].Value;

                //Cast transformation matrix
                var boneMatrix = Matrix4.Identity;
                int i = 0;
                foreach (var v in ((KVObject)boneKV.Properties["m_invBindPose"].Value).Properties.Values)
                {
                    boneMatrix[i / 4, i % 4] = Convert.ToSingle(v.Value);
                    i++;
                }

                //Create bone
                var bone = new Bone(boneName, boneMatrix);

                // Set parent
                if (parentName.Length > 0)
                {
                    var parent = Bones[parentName];
                    bone.SetParent(parent);

                    // Link bone as child of parent
                    parent.AddChild(bone);
                }

                //Add to dictionary
                Bones[boneName] = bone;
            }
        }
    }
}
