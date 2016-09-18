using System;
using System.Collections.Generic;
using OpenTK;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;
using Vector3 = ValveResourceFormat.ResourceTypes.NTROSerialization.Vector3;
using Vector4 = ValveResourceFormat.ResourceTypes.NTROSerialization.Vector4;

namespace GUI.Types.Renderer.Animation
{
    internal class Skeleton
    {
        public List<Bone> Roots { get; set; }
        public Bone[] Bones { get; set; }

        public Skeleton()
        {
            Bones = new Bone[0];
        }

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

            // Get the remap table and invert it for our construction method
            var remapTable = ((NTROArray)modelData.Output["m_remappingTable"]).ToArray<short>();
            var invMapTable = new Dictionary<int, int>();
            for (var i = 0; i < remapTable.Length; i++)
            {
                if (!invMapTable.ContainsKey(remapTable[i]))
                {
                    invMapTable.Add(remapTable[i], i);
                }
            }

            // Construct the armature from the skeleton KV
            ConstructFromNTRO(((NTROValue<NTROStruct>)modelData.Output["m_modelSkeleton"]).Value, invMapTable);
        }

        public void DebugDraw(DebugUtil debug)
        {
            foreach (var root in Roots)
            {
                DebugDrawRecursive(debug, root, Matrix4.Identity);
            }
        }

        private void DebugDrawRecursive(DebugUtil debug, Bone bone, Matrix4 matrix)
        {
            var transform = bone.BindPose * matrix;

            debug.AddCube(transform);

            foreach (var child in bone.Children)
            {
                DebugDrawRecursive(debug, child, transform);
            }
        }

        // Construct the Armature object from mesh skeleton KV data.
        public void ConstructFromNTRO(NTROStruct skeletonData, Dictionary<int, int> remapTable)
        {
            var boneNames = skeletonData.Get<NTROArray>("m_boneName").ToArray<string>();
            var boneParents = skeletonData.Get<NTROArray>("m_nParent").ToArray<short>();
            var bonePositions = skeletonData.Get<NTROArray>("m_bonePosParent").ToArray<Vector3>();
            var boneRotations = skeletonData.Get<NTROArray>("m_boneRotParent").ToArray<Vector4>();

            // Initialise bone array
            Bones = new Bone[boneNames.Length];

            //Add all bones to the list
            for (var i = 0; i < boneNames.Length; i++)
            {
                var name = boneNames[i];

                var position = new OpenTK.Vector3(bonePositions[i].X, bonePositions[i].Y, bonePositions[i].Z);
                var rotation = new Quaternion(boneRotations[i].X, boneRotations[i].Y, boneRotations[i].Z, boneRotations[i].W);

                // Create bone
                var index = remapTable.ContainsKey(i) ? remapTable[i] : -1;
                var bone = new Bone(name, index, position, rotation);

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
