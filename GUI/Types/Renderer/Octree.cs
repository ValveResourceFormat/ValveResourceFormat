using System;
using System.Collections.Generic;
using OpenTK;

namespace GUI.Types.Renderer
{
    internal class Octree<T>
        where T : IOctreeElement
    {
        private const int MaximumElementsBeforeSubdivide = 4;
        private const float MinimumNodeSize = 64.0f;

        public class Node
        {
            public AABB Region { get; set; }

            public List<T> Elements { get; private set; }
            public Node[] Children { get; private set; }

            public void Subdivide()
            {
                if (Children != null)
                {
                    // Already subdivided
                    return;
                }

                var subregionSize = Region.Size * 0.5f;
                var myCenter = Region.Min + subregionSize;

                Children = new Node[8];
                Children[0] = new Node(Region.Min, subregionSize);
                Children[1] = new Node(new Vector3(myCenter.X, Region.Min.Y, Region.Min.Z), subregionSize);
                Children[2] = new Node(new Vector3(Region.Min.X, myCenter.Y, Region.Min.Z), subregionSize);
                Children[3] = new Node(new Vector3(myCenter.X, myCenter.Y, Region.Min.Z), subregionSize);
                Children[4] = new Node(new Vector3(Region.Min.X, Region.Min.Y, myCenter.Z), subregionSize);
                Children[5] = new Node(new Vector3(myCenter.X, Region.Min.Y, myCenter.Z), subregionSize);
                Children[6] = new Node(new Vector3(Region.Min.X, myCenter.Y, myCenter.Z), subregionSize);
                Children[7] = new Node(new Vector3(myCenter.X, myCenter.Y, myCenter.Z), subregionSize);

                var remainingElements = new List<T>();
                foreach (var element in Elements)
                {
                    var elementBB = element.BoundingBox;
                    bool movedDown = false;

                    foreach (var child in Children)
                    {
                        if (child.Region.Contains(elementBB))
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

            public Node(Vector3 regionMin, Vector3 regionSize)
            {
                Region = new AABB(regionMin, regionMin + regionSize);
            }

            public bool HasChildren { get => Children != null; }
            public bool HasElements { get => Elements != null && Elements.Count > 0; }

            public void Insert(T element)
            {
                if (!HasChildren && HasElements && Region.Size.X > MinimumNodeSize && Elements.Count >= MaximumElementsBeforeSubdivide)
                {
                    Subdivide();
                }

                bool inserted = false;

                if (HasChildren)
                {
                    var elementBB = element.BoundingBox;

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
                    if (Elements == null)
                    {
                        Elements = new List<T>();
                    }

                    Elements.Add(element);
                }
            }

            public bool Remove(T element)
            {
                if (HasElements)
                {
                    if (Elements.Remove(element))
                    {
                        return true;
                    }
                }

                if (HasChildren)
                {
                    var elementBB = element.BoundingBox;

                    foreach (var child in Children)
                    {
                        if (child.Region.Contains(elementBB) && child.Remove(element))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            public void Clear()
            {
                Elements = null;
                Children = null;
            }

            public void Query(AABB boundingBox, List<T> results)
            {
                if (HasElements)
                {
                    foreach (var element in Elements)
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

            public void Query(Frustum frustum, List<T> results)
            {
                if (HasElements)
                {
                    foreach (var element in Elements)
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
                        if (frustum.Intersects(child.Region))
                        {
                            child.Query(frustum, results);
                        }
                    }
                }
            }
        }

        public Node Root { get; private set; }

        public Octree(float size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            Root = new Node(new Vector3(-size * 0.5f), new Vector3(size));
        }

        public void Insert(T element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            Root.Insert(element);
        }

        public void Remove(T element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            Root.Remove(element);
        }

        public void Clear()
        {
            Root.Clear();
        }

        public IEnumerable<T> Query(AABB boundingBox)
        {
            var results = new List<T>();
            Root.Query(boundingBox, results);
            return results;
        }

        public IEnumerable<T> Query(Frustum frustum)
        {
            var results = new List<T>();
            Root.Query(frustum, results);
            return results;
        }

        public class TestElement : IOctreeElement
        {
            public AABB BoundingBox { get; private set; }

            public TestElement(Vector3 center, float size)
            {
                BoundingBox = new AABB(center - new Vector3(size * 0.5f), center + new Vector3(size * 0.5f));
            }
        }
    }

    internal interface IOctreeElement
    {
        AABB BoundingBox { get; }
    }
}
