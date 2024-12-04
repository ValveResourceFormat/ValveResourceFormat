using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class Octree<T>
        where T : class
    {
        private const int OptimalElementCountLarge = 4;
        private const int OptimalElementCountSmall = 32;
        private const float MinimumNodeSize = 64.0f;

        public struct Element
        {
            public T ClientObject;
            public AABB BoundingBox;
        }

        public class Node
        {
            public Node Parent { get; }
            public AABB Region { get; }

            public List<Element> Elements { get; private set; }
            public Node[] Children { get; private set; } = [];

            public bool FrustumCulled { get; set; }
            public int OcclusionQueryHandle { get; set; } = -1;
            public bool OcculsionQuerySubmitted { get; set; }
            public bool OcclusionCulled { get; set; }

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

                var remainingElements = new List<Element>();
                foreach (var element in Elements)
                {
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
                        remainingElements.Add(element);
                    }
                }

                Elements = remainingElements;
            }

            public Node(Node parent, Vector3 regionMin, Vector3 regionSize)
            {
                Parent = parent;
                Region = new AABB(regionMin, regionMin + regionSize);
            }

            public bool HasChildren => Children.Length > 0;
            public bool HasElements => Elements != null && Elements.Count > 0;

            public void Insert(Element element)
            {
                if (!HasChildren && HasElements && ShouldSubdivide(Region.Size.X, Elements.Count))
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

                if (!inserted)
                {
                    Elements ??= [];

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

            public (Node Node, int Index) Find(T clientObject, in AABB bounds)
            {
                if (HasElements)
                {
                    for (var i = 0; i < Elements.Count; ++i)
                    {
                        if (Elements[i].ClientObject == clientObject)
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

            public void Query(in AABB boundingBox, List<T> results)
            {
                if (HasElements)
                {
                    foreach (var element in Elements)
                    {
                        if (element.BoundingBox.Intersects(boundingBox))
                        {
                            results.Add(element.ClientObject);
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

            public void Query(Frustum frustum, List<T> results)
            {
                if (HasElements)
                {
                    foreach (var element in Elements)
                    {
                        if (frustum.Intersects(element.BoundingBox))
                        {
                            results.Add(element.ClientObject);
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

            public AABB GetBounds()
            {
                var mins = new Vector3(float.MaxValue);
                var maxs = new Vector3(-float.MaxValue);

                if (HasElements)
                {
                    foreach (var element in Elements)
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

        public Node Root { get; }

        public Octree(float size)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

            Root = new Node(null, new Vector3(-size * 0.5f), new Vector3(size));
        }

        public void Insert(T obj, in AABB bounds)
        {
            ArgumentNullException.ThrowIfNull(obj);

            Root.Insert(new Element { ClientObject = obj, BoundingBox = bounds });
        }

        public void Remove(T obj, in AABB bounds)
        {
            ArgumentNullException.ThrowIfNull(obj);

            var (node, index) = Root.Find(obj, bounds);
            node?.Elements.RemoveAt(index);
        }

        public void Update(T obj, in AABB oldBounds, in AABB newBounds)
        {
            ArgumentNullException.ThrowIfNull(obj);

            var (node, index) = Root.Find(obj, oldBounds);
            if (node != null)
            {
                // Locate the closest ancestor that the new bounds fit inside
                var ancestor = node;
                while (ancestor.Parent != null && !ancestor.Region.Contains(newBounds))
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
                            if (child.Region.Contains(newBounds))
                            {
                                node.Elements.RemoveAt(index);
                                child.Insert(new Element { ClientObject = obj, BoundingBox = newBounds });
                                return;
                            }
                        }
                    }

                    // Not pushed down into any children
                    node.Elements[index] = new Element { ClientObject = obj, BoundingBox = newBounds };
                }
                else
                {
                    node.Elements.RemoveAt(index);
                    ancestor.Insert(new Element { ClientObject = obj, BoundingBox = newBounds });
                }
            }
        }

        public void Clear()
        {
            Root.Clear();
        }
    }
}
