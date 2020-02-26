using System;
using System.Collections.Generic;
using System.Numerics;

namespace GUI.Types.Renderer
{
    internal class Octree<T>
        where T : class
    {
        private const int MaximumElementsBeforeSubdivide = 4;
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
                    bool movedDown = false;

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

            public bool HasChildren { get => Children != null; }
            public bool HasElements { get => Elements != null && Elements.Count > 0; }

            public void Insert(Element element)
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
                        Elements = new List<Element>();
                    }

                    Elements.Add(element);
                }
            }

            public (Node, int) Find(T clientObject, AABB bounds)
            {
                if (HasElements)
                {
                    for (int i = 0; i < Elements.Count; ++i)
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

            Root = new Node(null, new Vector3(-size * 0.5f), new Vector3(size));
        }

        public void Insert(T obj, AABB bounds)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            Root.Insert(new Element { ClientObject = obj, BoundingBox = bounds });
        }

        public void Remove(T obj, AABB bounds)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var (node, index) = Root.Find(obj, bounds);
            if (node != null)
            {
                node.Elements.RemoveAt(index);
            }
        }

        public void Update(T obj, AABB oldBounds, AABB newBounds)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

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

        public List<T> Query(AABB boundingBox)
        {
            var results = new List<T>();
            Root.Query(boundingBox, results);
            return results;
        }

        public List<T> Query(Frustum frustum)
        {
            var results = new List<T>();
            Root.Query(frustum, results);
            return results;
        }
    }
}
