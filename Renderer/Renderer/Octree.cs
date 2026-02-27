using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Spatial partitioning structure for efficient scene node culling and queries.
    /// </summary>
    public class Octree
    {
        private const int OptimalElementCountLarge = 4;
        private const int OptimalElementCountSmall = 32;
        private const float MinimumNodeSize = 64.0f;

        /// <summary>
        /// A single node in the octree containing elements and child nodes.
        /// </summary>
        public class Node
        {
            /// <summary>
            /// Gets the parent node, or <see langword="null"/> if this is the root.
            /// </summary>
            public Node? Parent { get; }

            /// <summary>
            /// Gets the spatial region covered by this node.
            /// </summary>
            public AABB Region { get; }

            /// <summary>
            /// Gets the scene nodes stored directly in this node.
            /// </summary>
            public List<SceneNode>? Elements { get; private set; }

            /// <summary>
            /// Gets the eight child octree nodes created after subdivision.
            /// </summary>
            public Node[] Children { get; private set; } = [];

            /// <summary>
            /// Gets or sets whether this node is outside the view frustum.
            /// </summary>
            public bool FrustumCulled { get; set; }

            /// <summary>
            /// Gets or sets the OpenGL occlusion query handle for this node.
            /// </summary>
            public int OcclusionQueryHandle { get; set; } = -1;

            /// <summary>
            /// Gets or sets whether an occlusion query has been submitted for this node.
            /// </summary>
            public bool OcculsionQuerySubmitted { get; set; }

            /// <summary>
            /// Gets or sets whether this node is occluded by other geometry.
            /// </summary>
            public bool OcclusionCulled { get; set; }

            /// <summary>
            /// Splits this node into eight child nodes and redistributes elements.
            /// </summary>
            /// <remarks>
            /// Elements that fit entirely within a child node are moved down.
            /// Elements spanning multiple children remain in this node.
            /// </remarks>
            public void Subdivide()
            {
                if (HasChildren)
                {
                    // Already subdivided
                    return;
                }

                var subregionSize = Region.Size * 0.5f;
                var myCenter = Region.Min + subregionSize;

                Children = new Node[8];
                Children[0] = new Node(this, Region.Min, subregionSize);
                Children[1] = new Node(this, new Vector3(myCenter.X, Region.Min.Y, Region.Min.Z), subregionSize);
                Children[2] = new Node(this, new Vector3(Region.Min.X, myCenter.Y, Region.Min.Z), subregionSize);
                Children[3] = new Node(this, new Vector3(myCenter.X, myCenter.Y, Region.Min.Z), subregionSize);
                Children[4] = new Node(this, new Vector3(Region.Min.X, Region.Min.Y, myCenter.Z), subregionSize);
                Children[5] = new Node(this, new Vector3(myCenter.X, Region.Min.Y, myCenter.Z), subregionSize);
                Children[6] = new Node(this, new Vector3(Region.Min.X, myCenter.Y, myCenter.Z), subregionSize);
                Children[7] = new Node(this, new Vector3(myCenter.X, myCenter.Y, myCenter.Z), subregionSize);

                var writeIndex = 0;
                for (var i = 0; i < Elements!.Count; i++)
                {
                    var element = Elements[i];
                    var movedDown = false;

                    foreach (var child in Children)
                    {
                        if (child.Region.Contains(element.BoundingBox))
                        {
                            child.Insert(element);
                            movedDown = true;
                            break;
                        }
                    }

                    if (!movedDown)
                    {
                        Elements[writeIndex++] = element;
                    }
                }

                if (writeIndex < Elements.Count)
                {
                    Elements.RemoveRange(writeIndex, Elements.Count - writeIndex);
                }
            }

            /// <summary>
            /// Initializes a new octree node.
            /// </summary>
            /// <param name="parent">Parent node, or <see langword="null"/> for root.</param>
            /// <param name="regionMin">Minimum corner of the node's region.</param>
            /// <param name="regionSize">Size of the node's region in each dimension.</param>
            public Node(Node? parent, Vector3 regionMin, Vector3 regionSize)
            {
                Parent = parent;
                Region = new AABB(regionMin, regionMin + regionSize);
            }

            /// <summary>
            /// Gets whether this node has been subdivided into child nodes.
            /// </summary>
            public bool HasChildren => Children.Length > 0;

            /// <summary>
            /// Gets whether this node contains any scene elements.
            /// </summary>
            public bool HasElements => Elements != null && Elements.Count > 0;

            /// <summary>
            /// Inserts a scene node into this octree node or an appropriate child.
            /// </summary>
            /// <param name="element">Scene node to insert.</param>
            /// <remarks>
            /// Automatically subdivides the node if element density exceeds threshold.
            /// Elements are pushed down to child nodes when they fit entirely within a child's region.
            /// </remarks>
            public void Insert(SceneNode element)
            {
                if (!HasChildren && HasElements && ShouldSubdivide(Region.Size.X, Elements!.Count))
                {
                    Subdivide();
                }

                var inserted = false;

                if (HasChildren)
                {
                    var elementBB = element.BoundingBox;

                    // Setting a minimum size prevents inserting element on wrong region
                    const float MinimumSize = 0.05f;
                    var adjustedSize = Vector3.Max(elementBB.Size, new Vector3(MinimumSize)) - elementBB.Size;
                    if (adjustedSize.LengthSquared() > 0.0f)
                    {
                        elementBB = new AABB(elementBB.Min - adjustedSize * 0.5f, elementBB.Max + adjustedSize * 0.5f);
                    }

                    foreach (var child in Children)
                    {
                        if (child.Region.Contains(elementBB))
                        {
                            inserted = true;
                            child.Insert(element);
                            break;
                        }
                    }
                }

                if (inserted)
                {
                    return;
                }

                if (Elements == null)
                {
                    Elements = [element];
                }
                else
                {
                    Elements.Add(element);
                }
            }

            private static bool ShouldSubdivide(float size, int count)
            {
                if (size <= MinimumNodeSize)
                {
                    return false;
                }

                var sizeNormalized = MathF.Pow(MinimumNodeSize / size, 4.0f);
                var optimalCount = (int)float.Lerp(OptimalElementCountLarge, OptimalElementCountSmall, sizeNormalized);

                return count >= optimalCount;
            }

            /// <summary>
            /// Finds a scene node within this node or its children.
            /// </summary>
            /// <param name="clientObject">Scene node to locate.</param>
            /// <param name="bounds">Bounding box of the scene node.</param>
            /// <returns>Tuple containing the node where the element was found and its index, or (<see langword="null"/>, -1) if not found.</returns>
            public (Node? Node, int Index) Find(SceneNode clientObject, in AABB bounds)
            {
                if (HasElements)
                {
                    for (var i = 0; i < Elements!.Count; ++i)
                    {
                        if (Elements[i] == clientObject)
                        {
                            return (this, i);
                        }
                    }
                }

                if (HasChildren)
                {
                    foreach (var child in Children)
                    {
                        if (child.Region.Contains(bounds))
                        {
                            return child.Find(clientObject, bounds);
                        }
                    }
                }

                return (null, -1);
            }

            /// <summary>
            /// Clears all elements and children from this node and releases OpenGL resources.
            /// </summary>
            public void Clear()
            {
                Elements = null;
                Children = [];

                if (OcclusionQueryHandle != -1)
                {
                    GL.DeleteQuery(OcclusionQueryHandle);
                    OcclusionQueryHandle = -1;
                }

                FrustumCulled = false;
                OcculsionQuerySubmitted = false;
                OcclusionCulled = false;
            }

            /// <summary>
            /// Queries scene nodes that intersect with the specified bounding box.
            /// </summary>
            /// <param name="boundingBox">Bounding box to test against.</param>
            /// <param name="results">List to populate with intersecting scene nodes.</param>
            public void Query(in AABB boundingBox, List<SceneNode> results)
            {
                if (HasElements)
                {
                    foreach (var element in Elements!)
                    {
                        if (element.BoundingBox.Intersects(boundingBox))
                        {
                            results.Add(element);
                        }
                    }
                }

                if (HasChildren)
                {
                    foreach (var child in Children)
                    {
                        if (child.Region.Intersects(boundingBox))
                        {
                            child.Query(boundingBox, results);
                        }
                    }
                }
            }

            /// <summary>
            /// Queries scene nodes visible within the specified view frustum.
            /// </summary>
            /// <param name="frustum">View frustum to test against.</param>
            /// <param name="results">List to populate with visible scene nodes.</param>
            /// <remarks>
            /// Performs frustum and occlusion culling during traversal.
            /// </remarks>
            public void Query(Frustum frustum, List<SceneNode> results)
            {
                if (HasElements)
                {
                    foreach (var element in Elements!)
                    {
                        if (frustum.Intersects(element.BoundingBox))
                        {
                            results.Add(element);
                        }
                    }
                }

                if (HasChildren)
                {
                    foreach (var child in Children)
                    {
                        child.FrustumCulled = !frustum.Intersects(child.Region);

                        if (child.FrustumCulled || child.OcclusionCulled)
                        {
                            continue;
                        }

                        child.Query(frustum, results);
                    }
                }
            }

            /// <summary>
            /// Calculates the combined bounding box of all elements in this node and its children.
            /// </summary>
            /// <returns>Axis-aligned bounding box encompassing all contained elements.</returns>
            public AABB GetBounds()
            {
                var mins = new Vector3(float.MaxValue);
                var maxs = new Vector3(-float.MaxValue);

                if (HasElements)
                {
                    foreach (var element in Elements!)
                    {
                        mins = Vector3.Min(mins, element.BoundingBox.Min);
                        maxs = Vector3.Max(maxs, element.BoundingBox.Max);
                    }
                }

                if (HasChildren)
                {
                    foreach (var child in Children)
                    {
                        var childBounds = child.GetBounds();
                        mins = Vector3.Min(mins, childBounds.Min);
                        maxs = Vector3.Max(maxs, childBounds.Max);
                    }
                }

                return new AABB(mins, maxs);
            }
        }

        /// <summary>
        /// Gets the root node of the octree.
        /// </summary>
        public Node Root { get; private set; }

        /// <summary>
        /// Gets or sets whether this octree needs to be rebuilt.
        /// </summary>
        public bool Dirty { get; set; } = true;

        public OctreeDebugRenderer? DebugRenderer { get; set; }

        /// <summary>
        /// Initializes a new octree with the specified size.
        /// </summary>
        /// <param name="size">Total size of the octree region (centered at origin).</param>
        public Octree(float size)
            : this(new AABB(Vector3.Zero, size / 2f))
        {
        }

        /// <summary>
        /// Initializes a new octree with the specified bounding box as the largest region.
        /// </summary>
        public Octree(AABB size)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Size.Length());

            Root = new Node(null, size.Min, size.Size);
        }

        /// <summary>
        /// Inserts a scene node into the octree.
        /// </summary>
        /// <param name="obj">Scene node to insert.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is <see langword="null"/>.</exception>
        public void Insert(SceneNode obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            Root.Insert(obj);
        }

        /// <summary>
        /// Removes a scene node from the octree.
        /// </summary>
        /// <param name="obj">Scene node to remove.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is <see langword="null"/>.</exception>
        public void Remove(SceneNode obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            var (node, index) = Root.Find(obj, obj.BoundingBox);
            node?.Elements?.RemoveAt(index);
        }

        /// <summary>
        /// Updates a scene node's position in the octree after its bounds have changed.
        /// </summary>
        /// <param name="obj">Scene node to update.</param>
        /// <param name="oldBounds">Previous bounding box of the scene node.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Relocates the node to the appropriate octree node based on its new bounds.
        /// May push the node down to child nodes or up to ancestor nodes as needed.
        /// </remarks>
        public void Update(SceneNode obj, in AABB oldBounds)
        {
            ArgumentNullException.ThrowIfNull(obj);

            var (node, index) = Root.Find(obj, oldBounds);
            if (node is { Elements: not null })
            {
                // Locate the closest ancestor that the new bounds fit inside
                var ancestor = node;
                while (ancestor.Parent != null && !ancestor.Region.Contains(obj.BoundingBox))
                {
                    ancestor = ancestor.Parent;
                }

                // Still fits in same node?
                if (ancestor == node)
                {
                    // Still check for pushdown
                    if (node.HasChildren)
                    {
                        foreach (var child in node.Children)
                        {
                            if (child.Region.Contains(obj.BoundingBox))
                            {
                                node.Elements.RemoveAt(index);
                                child.Insert(obj);
                                return;
                            }
                        }
                    }

                    // Not pushed down into any children
                    node.Elements[index] = obj;
                }
                else
                {
                    node.Elements.RemoveAt(index);
                    ancestor.Insert(obj);
                }
            }
        }

        /// <summary>
        /// Clears all nodes and elements from the octree.
        /// </summary>
        public void Clear()
        {
            Root.Clear();
        }


        /// <summary>
        /// Clears all nodes and roughly sizes root to the specified bounds.
        /// </summary>
        public void Clear(AABB rootBounds)
        {
            Clear();

            var min = Vector3.Max(-new Vector3(16384), rootBounds.Min);
            var max = Vector3.Min(new Vector3(16384), rootBounds.Max);

            max = new Vector3(max.Length());
            Root = new Node(null, min, max - min);
        }
    }
}
