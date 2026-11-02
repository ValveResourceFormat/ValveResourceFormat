namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// The bones of a skeleton in depth first order, so every bone comes after its parent and each
    /// subtree is a contiguous range.
    /// </summary>
    internal sealed class BoneHierarchy
    {
        public Bone[] Bones { get; }

        /// <summary>Bone indices in depth first order.</summary>
        public int[] Order { get; }

        /// <summary>The bone index of the parent of each entry of <see cref="Order"/>, or -1 for a root.</summary>
        public int[] ParentOfOrder { get; }

        /// <summary>
        /// Orders the bones below <paramref name="roots"/> and gives each of them its place in the order.
        /// </summary>
        public static void Attach(Bone[] bones, Bone[] roots) => _ = new BoneHierarchy(bones, roots);

        private BoneHierarchy(Bone[] bones, Bone[] roots)
        {
            Bones = bones;

            var order = new List<int>(bones.Length);
            var parentOfOrder = new List<int>(bones.Length);

            foreach (var root in roots)
            {
                Add(root, -1, order, parentOfOrder);
            }

            Order = [.. order];
            ParentOfOrder = [.. parentOfOrder];
        }

        private void Add(Bone bone, int parentIndex, List<int> order, List<int> parentOfOrder)
        {
            bone.Hierarchy = this;
            bone.HierarchyStart = order.Count;

            order.Add(bone.Index);
            parentOfOrder.Add(parentIndex);

            foreach (var child in bone.Children)
            {
                Add(child, bone.Index, order, parentOfOrder);
            }

            bone.HierarchyEnd = order.Count;
        }
    }
}
