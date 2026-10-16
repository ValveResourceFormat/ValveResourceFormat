namespace ValveResourceFormat.IO;

/// <summary>Solvers shared by the cloth reconstruction and the cloth extract.</summary>
internal static class ClothMath
{
    /// <summary>
    /// One connected component of pair-sum equations <c>p[a] + p[b] = sum</c>, each member written as
    /// <c>Sign * free + Offset</c> of one free parameter.
    /// </summary>
    /// <param name="Members">The members in the order the walk reached them, the first with sign 1 and offset 0.</param>
    /// <param name="Sign">Each member's sign, 1 or -1.</param>
    /// <param name="Offset">Each member's offset.</param>
    /// <param name="Forced">The values of the free parameter the component's odd cycles force, in the order the walk closed them.</param>
    internal sealed record PairSumComponent(List<int> Members, Dictionary<int, float> Sign, Dictionary<int, float> Offset,
        List<float> Forced);

    /// <summary>Gets each node's pair-sum equations as the other node and the sum, in equation order.</summary>
    internal static Dictionary<int, List<(int Other, float Sum)>> PairSumAdjacency(IEnumerable<(int A, int B, float Sum)> equations)
    {
        var adjacency = new Dictionary<int, List<(int Other, float Sum)>>();
        foreach (var (a, b, sum) in equations)
        {
            ClothReconstruction.GetOrAdd(adjacency, a).Add((b, sum));
            ClothReconstruction.GetOrAdd(adjacency, b).Add((a, sum));
        }

        return adjacency;
    }

    /// <summary>
    /// Splits pair-sum equations into their connected components, starting one at each of <paramref name="roots"/> no
    /// earlier component reached and walking it depth first or breadth first. Returns null when an even cycle's sums
    /// differ by more than <paramref name="tolerance"/>.
    /// </summary>
    internal static List<PairSumComponent>? PairSumComponents(Dictionary<int, List<(int Other, float Sum)>> adjacency,
        IEnumerable<int> roots, float tolerance, bool depthFirst)
    {
        var components = new List<PairSumComponent>();
        var reached = new HashSet<int>();
        var frontier = new List<int>();
        foreach (var root in roots)
        {
            if (!reached.Add(root))
            {
                continue;
            }

            var component = new PairSumComponent([root], new() { [root] = 1f }, new() { [root] = 0f }, []);
            components.Add(component);
            var (sign, offset) = (component.Sign, component.Offset);
            frontier.Clear();
            frontier.Add(root);
            var head = 0;
            while (head < frontier.Count)
            {
                int node;
                if (depthFirst)
                {
                    node = frontier[^1];
                    frontier.RemoveAt(frontier.Count - 1);
                }
                else
                {
                    node = frontier[head++];
                }

                foreach (var (other, sum) in adjacency.GetValueOrDefault(node) ?? [])
                {
                    var otherSign = -sign[node];
                    var otherOffset = sum - offset[node];
                    if (sign.TryGetValue(other, out var known))
                    {
                        if (known == otherSign)
                        {
                            if (MathF.Abs(otherOffset - offset[other]) > tolerance)
                            {
                                return null;
                            }
                        }
                        else
                        {
                            component.Forced.Add((otherOffset - offset[other]) / (2f * known));
                        }

                        continue;
                    }

                    sign[other] = otherSign;
                    offset[other] = otherOffset;
                    component.Members.Add(other);
                    reached.Add(other);
                    frontier.Add(other);
                }
            }
        }

        return components;
    }
}
