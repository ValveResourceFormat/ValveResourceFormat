using System.Linq;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>Whether every pin <c>flex_cloth_borders</c> would free on <paramref name="proxy"/> has an <c>m_NodeBases</c> entry.</summary>
    internal static bool FlexedPinsCarryNodeBases(ClothReconstruction cloth, ProxyMesh proxy)
        => FlexedPinNodes(cloth, proxy).All(cloth.Index.NodeBases.ContainsKey);

    /// <summary>
    /// Whether <paramref name="proxy"/> has pins <c>flex_cloth_borders</c> would free and all of them have an
    /// <c>m_NodeBases</c> entry, which only that flag gives a pinned proxy vertex.
    /// </summary>
    internal static bool FlexedPinsStateClothBorders(ClothReconstruction cloth, ProxyMesh proxy)
        => FlexedPinNodes(cloth, proxy).Any() && FlexedPinsCarryNodeBases(cloth, proxy);

    /// <summary>The pinned corners of every face with two or more simulated corners.</summary>
    private static IEnumerable<int> FlexedPinNodes(ClothReconstruction cloth, ProxyMesh proxy)
    {
        foreach (var face in proxy.Faces)
        {
            if (!FaceFlexes(proxy, face))
            {
                continue;
            }

            foreach (var corner in face)
            {
                var node = proxy.NodeIndices[corner];
                if (proxy.ClothEnable[corner] == 0f && node < cloth.Fe.StaticNodes)
                {
                    yield return node;
                }
            }
        }
    }

    private static bool FaceFlexes(ProxyMesh proxy, int[] face)
        => face.Distinct().Count(corner => proxy.ClothEnable[corner] != 0f) >= 2;

    /// <summary>
    /// Whether <paramref name="proxy"/> states <c>flex_cloth_borders</c>: every pin the flag frees is recorded
    /// rotation-free and every other pin rotation-locked. A back-solving sheet frees the pins with a registered skin
    /// influence instead, and unless the pins' node bases state the flag, each freed pin's static anchor needs a
    /// position-driven skeleton parent.
    /// </summary>
    internal static bool ProxyFlexesClothBorders(ClothReconstruction cloth, ProxyMesh proxy, bool proxyBackSolves,
        bool addsBonesToRenderMesh)
    {
        if (!proxyBackSolves && addsBonesToRenderMesh && !FlexedPinsCarryNodeBases(cloth, proxy))
        {
            return false;
        }

        var ctrlIndexByName = LookupsOf(cloth).NodeByNameIgnoreCase;
        var statedByPinNodeBases = FlexedPinsStateClothBorders(cloth, proxy);

        var faced = new HashSet<int>();
        var flexReaches = new HashSet<int>();
        foreach (var face in proxy.Faces)
        {
            faced.UnionWith(face);
            if (FaceFlexes(proxy, face))
            {
                flexReaches.UnionWith(face.Where(corner => proxy.ClothEnable[corner] == 0f));
            }
        }

        var freesAny = false;
        for (var v = 0; v < proxy.ClothEnable.Length; v++)
        {
            if (proxy.ClothEnable[v] != 0f)
            {
                continue;
            }

            var node = proxy.NodeIndices[v];

            if (!proxyBackSolves)
            {
                if (!faced.Contains(v) || node >= cloth.Fe.StaticNodes)
                {
                    continue;
                }

                if (flexReaches.Contains(v) != cloth.Index.AllowsRotation(node))
                {
                    return false;
                }

                freesAny |= flexReaches.Contains(v);
                continue;
            }

            var influences = cloth.RecoveredSkinWeights.TryGetValue(node, out var recovered) && recovered.Length > 0
                ? recovered
                : cloth.ResolveSkinBone(node) is { } skinBone ? [new(skinBone, 1f)] : [];

            var registeredAnchors = influences
                .Where(i => i.Weight > 0f && ctrlIndexByName.ContainsKey(i.Bone))
                .Select(i => ctrlIndexByName[i.Bone])
                .ToArray();

            if (!faced.Contains(v))
            {
                if (registeredAnchors.Length > 0)
                {
                    return false;
                }

                continue;
            }

            if (node >= cloth.Fe.StaticNodes)
            {
                continue;
            }

            if ((registeredAnchors.Length > 0) != cloth.Index.AllowsRotation(node))
            {
                return false;
            }

            if (!cloth.Index.AllowsRotation(node))
            {
                continue;
            }

            freesAny = true;

            if (statedByPinNodeBases)
            {
                continue;
            }

            foreach (var anchorNode in registeredAnchors)
            {
                if (!cloth.Index.IsStatic(anchorNode))
                {
                    continue;
                }

                if (cloth.SkeletonBoneParents?.GetValueOrDefault(cloth.Fe.CtrlName[anchorNode]) is { } parent
                    && (!ctrlIndexByName.TryGetValue(parent, out var parentNode)
                        || !cloth.IsPositionDriven(parentNode)))
                {
                    return false;
                }
            }
        }

        return freesAny;
    }
}
