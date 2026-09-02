using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a model skeleton with bones arranged in a hierarchy.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/ModelSkeletonData_t">ModelSkeletonData_t</seealso>
    public class Skeleton
    {
        /// <summary>
        /// Gets the name of the skeleton.
        /// </summary>
        public string Name { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the root bones of the skeleton.
        /// </summary>
        public Bone[] Roots { get; private set; } = [];

        /// <summary>
        /// Gets all bones in the skeleton.
        /// </summary>
        public Bone[] Bones { get; private set; } = [];

        /// <summary>
        /// Gets the root bone for cloth simulation, if present.
        /// </summary>
        public Bone? ClothSimulationRoot { get; private set; }

        /// <summary>
        /// Gets the authored bounding sphere radius of each bone, in model space, covering the vertices
        /// weighted to it. Zero for bones that carry no geometry.
        /// </summary>
        public float[] BoneSpheres { get; private set; } = [];

        /// <summary>
        /// Gets a bone by its <see cref="StringToken"/> hash.
        /// </summary>
        public Bone? this[uint hash]
        {
            get
            {
                var index = GetBoneIndex(hash);
                return index != -1 ? Bones[index] : null;
            }
        }

        /// <summary>
        /// Gets a bone by its name.
        /// </summary>
        public Bone? this[string name] => this[StringToken.Get(name)];

        /// <summary>
        /// Gets the index of a bone by its <see cref="StringToken"/> hash, or -1 if not found.
        /// </summary>
        public int GetBoneIndex(uint hash) => boneHashToIndex.TryGetValue(hash, out var index) ? index : -1;

        /// <summary>
        /// Gets the index of a bone by its name, or -1 if not found.
        /// </summary>
        public int GetBoneIndex(string name) => GetBoneIndex(StringToken.Get(name));

        /// <summary>
        /// Creates a skeleton from model data.
        /// </summary>
        public static Skeleton FromModelData(KVObject modelData)
        {
            if (!modelData.ContainsKey("m_modelSkeleton"))
            {
                Console.WriteLine("No skeleton data found.");
            }

            return new Skeleton(modelData.GetSubCollection("m_modelSkeleton"))
            {
                Name = modelData.GetStringProperty("m_name"),
            };
        }

        readonly Dictionary<uint, int> boneHashToIndex = [];

        /// <summary>
        /// Loads a compiled NM skeleton (.vnmskel) resource by name and builds a skeleton from it,
        /// or returns <see langword="null"/> when the resource cannot be loaded.
        /// </summary>
        public static Skeleton? FromSkeletonResource(IO.IFileLoader fileLoader, string skeletonName)
        {
            using var resource = fileLoader.LoadFileCompiled(skeletonName);
            return resource?.DataBlock is BinaryKV3 skeletonData
                ? FromSkeletonData(skeletonData.Data)
                : null;
        }

        /// <summary>
        /// Creates a skeleton from skeleton-specific data.
        /// </summary>
        public static Skeleton FromSkeletonData(KVObject nmSkeleton)
        {
            var boneNames = nmSkeleton.GetArray<string>("m_boneIDs");
            var boneParents = nmSkeleton.GetIntegerArray("m_parentIndices");
            var boneTransforms = nmSkeleton.GetArray("m_parentSpaceReferencePose");

            var boneCount = boneNames.Length;

            var s = new Skeleton
            {
                Name = nmSkeleton.GetStringProperty("m_ID"),
                Bones = new Bone[boneCount],
            };

            for (var i = 0; i < boneCount; i++)
            {
                var transform = boneTransforms[i].ToTransform();

                var bone = new Bone(i, boneNames[i], transform.Position, transform.Rotation, ModelSkeletonBoneFlags.NoBoneFlags);
                s.Bones[i] = bone;
            }

            s.SetBoneParents(boneParents);
            return s;
        }

        /// <summary>
        /// Builds a skeleton from a mesh's own render skeleton (<c>m_skeleton</c>, <c>CRenderSkeleton</c>),
        /// used to skin a standalone mesh that has no owning model. Bones are ordered as authored and
        /// parented by name; a bone naming an unknown parent, or an empty name, is treated as a root.
        /// Returns <see langword="null"/> when the render skeleton has no bones.
        /// </summary>
        public static Skeleton? FromRenderSkeleton(KVObject renderSkeleton)
        {
            var boneEntries = renderSkeleton.GetArray("m_bones");
            if (boneEntries.Count == 0)
            {
                return null;
            }

            var nameToIndex = new Dictionary<string, int>(boneEntries.Count);
            for (var i = 0; i < boneEntries.Count; i++)
            {
                nameToIndex[boneEntries[i].GetStringProperty("m_boneName")] = i;
            }

            var modelSpaceBindPose = new Matrix4x4[boneEntries.Count];
            var boneParents = new long[boneEntries.Count];

            for (var i = 0; i < boneEntries.Count; i++)
            {
                var boneData = boneEntries[i];
                var invBindPose = boneData.GetSubCollection("m_invBindPose").ToMatrix4x4();
                modelSpaceBindPose[i] = Matrix4x4.Invert(invBindPose, out var bindPose) ? bindPose : Matrix4x4.Identity;

                var parentName = boneData.GetStringProperty("m_parentName");
                boneParents[i] = !string.IsNullOrEmpty(parentName) && nameToIndex.TryGetValue(parentName, out var parentIndex)
                    ? parentIndex
                    : -1;
            }

            var boneSpheres = new float[boneEntries.Count];

            var s = new Skeleton
            {
                Bones = new Bone[boneEntries.Count],
                BoneSpheres = boneSpheres,
            };

            for (var i = 0; i < boneEntries.Count; i++)
            {
                var boneData = boneEntries[i];
                var name = boneData.GetStringProperty("m_boneName");
                boneSpheres[i] = boneData.GetFloatProperty("m_flSphereRadius");

                var parentIndex = boneParents[i];
                var localTransform = parentIndex >= 0 && Matrix4x4.Invert(modelSpaceBindPose[parentIndex], out var parentInverse)
                    ? modelSpaceBindPose[i] * parentInverse
                    : modelSpaceBindPose[i];

                Matrix4x4.Decompose(localTransform, out _, out var rotation, out var translation);

                s.Bones[i] = new Bone(i, name, translation, rotation, ModelSkeletonBoneFlags.NoBoneFlags);
            }

            s.SetBoneParents(boneParents);
            return s;
        }

        private Skeleton()
        {
        }

        /// <summary>
        /// Construct the Armature object from mesh skeleton KV data.
        /// </summary>
        private Skeleton(KVObject skeletonData)
        {
            var boneNames = skeletonData.GetArray<string>("m_boneName");
            var boneParents = skeletonData.GetIntegerArray("m_nParent");
            var boneFlags = skeletonData.GetIntegerArray("m_nFlag")
                .Select(flags => (ModelSkeletonBoneFlags)flags)
                .ToArray();
            var bonePositions = skeletonData.GetArray("m_bonePosParent").Select(v => v.ToVector3()).ToArray();
            var boneRotations = skeletonData.GetArray("m_boneRotParent").Select(v => v.ToQuaternion()).ToArray();

            var boneCount = boneNames.Length;
            Bones = new Bone[boneCount];

            if (skeletonData.ContainsKey("m_boneSphere"))
            {
                BoneSpheres = skeletonData.GetFloatArray("m_boneSphere");
            }

            for (var i = 0; i < boneCount; i++)
            {
                var bone = new Bone(i, boneNames[i], bonePositions[i], boneRotations[i], boneFlags[i]);
                Bones[i] = bone;

                if ((bone.Flags & ModelSkeletonBoneFlags.ProceduralCloth) == ModelSkeletonBoneFlags.Cloth
                && ClothSimulationRoot == null)
                {
                    ClothSimulationRoot = bone;
                }
            }

            SetBoneParents(boneParents);
        }

        private void SetBoneParents(long[] boneParents)
        {
            var roots = new List<Bone>();
            foreach (var bone in Bones)
            {
                var parentId = boneParents[bone.Index];
                if (parentId != -1)
                {
                    bone.SetParent(Bones[parentId]);
                    continue;
                }

                roots.Add(bone);
            }

            Roots = [.. roots];

            for (var i = 0; i < Bones.Length; i++)
            {
                var name = Bones[i].Name;
                var hash = StringToken.Store(name);
                boneHashToIndex[hash] = i;
            }
        }

        /// <summary>
        /// Accumulates each bone's world-space transform from the frame's local bone transforms,
        /// walking down the hierarchy from the roots. <paramref name="world"/> must be at least as
        /// long as <see cref="Bones"/> and is indexed by bone index.
        /// </summary>
        public void ComputeWorldPose(Frame frame, Span<Matrix4x4> world)
        {
            foreach (var root in Roots)
            {
                ComputeWorldSubtree(root, Matrix4x4.Identity, frame, world);
            }
        }

        /// <summary>
        /// Accumulates world-space transforms for one bone subtree under the given parent transform.
        /// A <see langword="null"/> <paramref name="frame"/> yields the bind pose.
        /// </summary>
        public static void ComputeWorldSubtree(Bone bone, Matrix4x4 parentWorld, Frame? frame, Span<Matrix4x4> world)
        {
            var local = bone.BindPose;

            if (frame != null)
            {
                var frameBone = frame.Bones[bone.Index];
                local = Matrix4x4.CreateScale(frameBone.Scale)
                    * Matrix4x4.CreateFromQuaternion(frameBone.Angle)
                    * Matrix4x4.CreateTranslation(frameBone.Position);
            }

            world[bone.Index] = local * parentWorld;

            foreach (var child in bone.Children)
            {
                ComputeWorldSubtree(child, world[bone.Index], frame, world);
            }
        }
    }
}
