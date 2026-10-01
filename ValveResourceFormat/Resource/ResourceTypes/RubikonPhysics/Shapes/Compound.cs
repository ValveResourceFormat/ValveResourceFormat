using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    /// <summary>Represents a compound shape: a mix of hulls, meshes, capsules and spheres under one bounding volume tree.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnCompound_t">RnCompound_t</seealso>
    public readonly struct Compound
    {
        /// <summary>
        /// Represents a node in the compound tree. Nodes are stored in pre-order: an internal node's first child is the next
        /// node and its second child follows the first child's subtree.
        /// </summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnCompoundTreeNode_t">RnCompoundTreeNode_t</seealso>
        public readonly struct TreeNode
        {
            /// <summary>The minimum bounds.</summary>
            public Vector3 Min { get; }

            /// <summary>The maximum bounds.</summary>
            public Vector3 Max { get; }

            /// <summary>The split axis of an internal node, or <see cref="Mesh.NodeType.Leaf"/>.</summary>
            public Mesh.NodeType Type { get; }

            /// <summary>For an internal node the number of nodes in its subtree including itself, for a leaf the child shape id.</summary>
            public uint SubtreeEndOrCompoundId { get; }

            /// <summary>Initializes a new instance of the <see cref="TreeNode"/> struct.</summary>
            public TreeNode(KVObject data)
            {
                Min = data.GetSubCollection("m_vMin").ToVector3();
                Max = data.GetSubCollection("m_vMax").ToVector3();
                Type = (Mesh.NodeType)data.GetUInt32Property("m_nType");
                SubtreeEndOrCompoundId = data.GetUInt32Property("m_nSubtreeEndOrCompoundId");
            }
        }

        /// <summary>Gets the hulls.</summary>
        public Hull[] Hulls { get; }

        /// <summary>Gets the meshes.</summary>
        public Mesh[] Meshes { get; }

        /// <summary>Gets the capsules.</summary>
        public Capsule[] Capsules { get; }

        /// <summary>Gets the spheres.</summary>
        public Sphere[] Spheres { get; }

        /// <summary>Gets the first hull's child shape id. Ids run over spheres, then capsules, hulls and meshes.</summary>
        public int HullBaseIndex { get; }

        /// <summary>Gets the first mesh's child shape id.</summary>
        public int MeshBaseIndex { get; }

        /// <summary>Gets the total number of child shapes.</summary>
        public int ShapeCount { get; }

        /// <summary>Gets the tree's start iteration index.</summary>
        public uint StartIterationIndex { get; }

        /// <summary>Gets the material index of each child shape, indexed by child shape id.</summary>
        public byte[] MaterialIndices { get; }

        /// <summary>Gets the minimum bounds.</summary>
        public Vector3 Min { get; }

        /// <summary>Gets the maximum bounds.</summary>
        public Vector3 Max { get; }

        /// <summary>Fraction 0..1 of coverage along YZ,ZX,XY sides of AABB</summary>
        public Vector3 OrthographicAreas { get; }

        /// <summary>Gets the surface area.</summary>
        public float SurfaceArea { get; }

        /// <summary>Gets the volume.</summary>
        public float Volume { get; }

        /// <summary>Gets the serialized keyvalues.</summary>
        public KVObject Data { get; }

        /// <summary>Initializes a new instance of the <see cref="Compound"/> struct.</summary>
        public Compound(KVObject data)
        {
            Data = data;
            Hulls = [.. data.GetArray("m_Hulls").Select(h => new Hull(h))];
            Meshes = [.. data.GetArray("m_Meshes").Select(m => new Mesh(m))];
            Capsules = [.. data.GetArray("m_Capsules").Select(c => new Capsule(c))];
            Spheres = [.. data.GetArray("m_Spheres").Select(s => new Sphere(s))];
            HullBaseIndex = data.GetInt32Property("m_nHullBaseIndex");
            MeshBaseIndex = data.GetInt32Property("m_nMeshBaseIndex");
            ShapeCount = data.GetInt32Property("m_nShapeCount");
            StartIterationIndex = data.GetSubCollection("m_Tree").GetUInt32Property("m_nStartIterationIndex");

            var materialIndices = data.GetArray<object>("m_CompoundMaterialIndices");
            MaterialIndices = materialIndices == null ? [] : [.. materialIndices.Select(Convert.ToByte)];

            var bounds = data.GetSubCollection("m_Bounds");
            Min = bounds.GetSubCollection("m_vMinBounds").ToVector3();
            Max = bounds.GetSubCollection("m_vMaxBounds").ToVector3();
            OrthographicAreas = data.GetSubCollection("m_vOrthographicAreas").ToVector3();
            SurfaceArea = data.GetFloatProperty("m_flSurfaceArea");
            Volume = data.GetFloatProperty("m_flVolume");
        }

        /// <summary>The nodes of the bounding volume tree over the child shapes.</summary>
        public TreeNode[] GetTreeNodes()
            => [.. Data.GetSubCollection("m_Tree").GetArray("m_Nodes").Select(n => new TreeNode(n))];
    }
}
