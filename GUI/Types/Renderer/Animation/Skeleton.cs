using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.KeyValues;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace GUI.Types.Renderer.Animation
{
    internal class Skeleton
    {
        public List<Bone> Roots { get; set; }
        public Bone[] Bones { get; set; }

        // Armature constructor
        public Skeleton(Resource model)
        {
            Bones = new Bone[0];
            Roots = new List<Bone>();

            var modelData = (NTRO)model.Blocks[BlockType.DATA];

            // Check if there is any skeleton data present at all
            if (!modelData.Output.Contains("m_modelSkeleton"))
            {
                Console.WriteLine("No skeleton data found.");
            }

            // Construct the armature from the skeleton KV
            ConstructFromNTRO(((NTROValue<NTROStruct>)modelData.Output["m_modelSkeleton"]).Value);
        }

        // Construct the Armature object from mesh skeleton KV data.
        public void ConstructFromNTRO(NTROStruct data)
        {
            var boneNames = data.Get<NTROArray>("m_boneName").ToArray<string>();
            var boneParents = data.Get<NTROArray>("m_nParent").ToArray<short>();
            var bonePositions = data.Get<NTROArray>("m_bonePosParent").ToArray<ValveResourceFormat.ResourceTypes.NTROSerialization.Vector3>();
            var boneRotations = data.Get<NTROArray>("m_boneRotParent").ToArray<ValveResourceFormat.ResourceTypes.NTROSerialization.Vector4>();

            // Initialise bone array
            Bones = new Bone[boneNames.Length];

            //Add all bones to the list
            for (int i = 0; i < boneNames.Length; i++)
            {
                var name = boneNames[i];

                var position = new OpenTK.Vector3(bonePositions[i].X, bonePositions[i].Y, bonePositions[i].Z);
                var rotation = new OpenTK.Quaternion(boneRotations[i].X, boneRotations[i].Y, boneRotations[i].Z, boneRotations[i].W);

                // Create bone
                var bone = new Bone(name, i, position, rotation);

                if (boneParents[i] != -1)
                {
                    bone.SetParent(Bones[boneParents[i]]);
                    Bones[boneParents[i]].AddChild(bone);
                }

                Bones[i] = bone;
            }

            FindRoots();
        }

        // Find all skeleton roots (bones without a parent)
        private void FindRoots()
        {
            // Create an empty root list
            Roots = new List<Bone>();

            foreach (var bone in Bones)
            {
                if (bone.Parent == null)
                {
                    Roots.Add(bone);
                }
            }
        }
    }
}
