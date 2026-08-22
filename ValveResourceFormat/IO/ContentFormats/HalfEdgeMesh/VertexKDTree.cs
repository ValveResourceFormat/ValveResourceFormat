namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

/// <summary>
/// Midpoint split kd-tree over a list of positions, for finding the vertices inside a box.
/// Taken from <see href="https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Scene/Components/Mesh/VertexKDTree.cs">Sbox</see>.
/// </summary>
internal sealed class VertexKDTree
{
    private sealed class Node
    {
        public int[] Children = [-1, -1];
        public int Axis = -1;
        public float Split;
        public int LeafStart, LeafCount;

        public bool IsLeaf => Axis == -1;
        public void InitAsSplit(float split, int axis) => (Axis, Split) = (axis, split);
        public void InitAsLeaf(int start, int count) => (Axis, LeafStart, LeafCount) = (-1, start, count);
    }

    private readonly List<Node> _tree = [];
    private IReadOnlyList<Vector3> _positions = [];
    private int[] _refs = [];

    public void BuildMidpoint(IReadOnlyList<Vector3> vertices)
    {
        _positions = vertices;
        _refs = new int[vertices.Count];
        for (var i = 0; i < _refs.Length; i++)
        {
            _refs[i] = i;
        }

        _tree.Clear();
        BuildNode(0, _refs.Length);
    }

    private int BuildNode(int start, int count)
    {
        if (count <= 8)
        {
            var nodeIndex = _tree.Count;
            _tree.Add(new Node());
            _tree[nodeIndex].InitAsLeaf(start, count);
            return nodeIndex;
        }

        ComputeBounds(out var min, out var max, start, count);
        var axis = GreatestAxis(max - min);
        var split = (Component(max, axis) + Component(min, axis)) * 0.5f;
        var splitIndex = FindMidpointIndex(start, count, axis, split);

        if (splitIndex == start || splitIndex == start + count)
        {
            var nodeIndex = _tree.Count;
            _tree.Add(new Node());
            _tree[nodeIndex].InitAsLeaf(start, count);
            return nodeIndex;
        }

        var idx = _tree.Count;
        _tree.Add(new Node { Axis = axis, Split = split });
        _tree[idx].Children[0] = BuildNode(start, splitIndex - start);
        _tree[idx].Children[1] = BuildNode(splitIndex, count - (splitIndex - start));
        return idx;
    }

    private int FindMidpointIndex(int start, int count, int axis, float split)
    {
        var mid = start + count / 2;
        var end = start + count;

        for (var i = mid; i < end; i++)
        {
            if (Component(_positions[_refs[i]], axis) < split)
            {
                (_refs[mid], _refs[i]) = (_refs[i], _refs[mid]);
                mid++;
            }
        }

        for (var i = mid - 1; i >= start; i--)
        {
            if (Component(_positions[_refs[i]], axis) >= split)
            {
                (_refs[mid - 1], _refs[i]) = (_refs[i], _refs[mid - 1]);
                mid--;
            }
        }

        return mid;
    }

    private void ComputeBounds(out Vector3 min, out Vector3 max, int start, int count)
    {
        min = max = _positions[_refs[start]];
        for (var i = start + 1; i < start + count; i++)
        {
            var p = _positions[_refs[i]];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
    }

    private static int GreatestAxis(Vector3 v) => v.X >= v.Y ? (v.X > v.Z ? 0 : 2) : (v.Y > v.Z ? 1 : 2);

    private static float Component(Vector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };

    public List<int> FindVertsInBox(Vector3 minBounds, Vector3 maxBounds)
    {
        var result = new List<int>();
        FindVertsInBoxRecursive(0, minBounds, maxBounds, result);
        return result;
    }

    private void FindVertsInBoxRecursive(int nodeIndex, Vector3 minBounds, Vector3 maxBounds, List<int> result)
    {
        if (nodeIndex < 0 || nodeIndex >= _tree.Count)
        {
            return;
        }

        var node = _tree[nodeIndex];

        if (node.IsLeaf)
        {
            for (var i = node.LeafStart; i < node.LeafStart + node.LeafCount; i++)
            {
                var idx = _refs[i];
                var p = _positions[idx];

                if (p.X >= minBounds.X && p.X <= maxBounds.X &&
                    p.Y >= minBounds.Y && p.Y <= maxBounds.Y &&
                    p.Z >= minBounds.Z && p.Z <= maxBounds.Z)
                {
                    result.Add(idx);
                }
            }
        }
        else
        {
            var axis = node.Axis;
            if (Component(minBounds, axis) <= node.Split)
            {
                FindVertsInBoxRecursive(node.Children[0], minBounds, maxBounds, result);
            }

            if (Component(maxBounds, axis) >= node.Split)
            {
                FindVertsInBoxRecursive(node.Children[1], minBounds, maxBounds, result);
            }
        }
    }
}
