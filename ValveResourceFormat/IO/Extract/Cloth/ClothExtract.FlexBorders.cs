using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// Whether every pin <c>flex_cloth_borders</c> would free on <paramref name="proxy"/>, a static corner of a face with
    /// two or more simulated corners, carries an <c>m_NodeBases</c> entry.
    /// </summary>
    internal static bool FlexedPinsCarryNodeBases(FeModel feModel, FeModel.ProxyMesh proxy)
    {
        foreach (var face in proxy.Faces)
        {
            if (face.Distinct().Count(corner => proxy.ClothEnable[corner] != 0f) < 2)
            {
                continue;
            }

            foreach (var corner in face)
            {
                var node = proxy.NodeIndices[corner];
                if (proxy.ClothEnable[corner] == 0f && node < feModel.StaticNodeCount && !feModel.NodeBases.ContainsKey(node))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the pins <c>flex_cloth_borders</c> would free on <paramref name="proxy"/> all carry an <c>m_NodeBases</c>
    /// entry, which only that flag gives a pinned proxy vertex.
    /// </summary>
    internal static bool FlexedPinsStateClothBorders(FeModel feModel, FeModel.ProxyMesh proxy)
    {
        var stated = false;
        foreach (var face in proxy.Faces)
        {
            if (face.Distinct().Count(corner => proxy.ClothEnable[corner] != 0f) < 2)
            {
                continue;
            }

            foreach (var corner in face)
            {
                var node = proxy.NodeIndices[corner];
                if (proxy.ClothEnable[corner] != 0f || node >= feModel.StaticNodeCount)
                {
                    continue;
                }

                if (!feModel.NodeBases.ContainsKey(node))
                {
                    return false;
                }

                stated = true;
            }
        }

        return stated;
    }

    /// <summary>
    /// Whether <paramref name="proxy"/> states <c>flex_cloth_borders</c>: every pin the flag frees is recorded
    /// rotation-free and every other pin rotation-locked. A back-solving sheet frees the pins with a registered skin
    /// influence instead, and unless the pins' node bases state the flag, each freed pin's static anchor needs a
    /// position-driven skeleton parent.
    /// </summary>
    internal static bool ProxyFlexesClothBorders(FeModel feModel, FeModel.ProxyMesh proxy, bool proxyBackSolves,
        bool addsBonesToRenderMesh)
    {
        if (!proxyBackSolves && addsBonesToRenderMesh && !FlexedPinsCarryNodeBases(feModel, proxy))
        {
            return false;
        }

        var ctrlIndexByName = new Dictionary<string, int>(feModel.CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < feModel.CtrlNames.Length; i++)
        {
            ctrlIndexByName.TryAdd(feModel.CtrlNames[i], i);
        }

        var statedByPinNodeBases = FlexedPinsStateClothBorders(feModel, proxy);

        var faced = new HashSet<int>();
        var flexReaches = new HashSet<int>();
        foreach (var face in proxy.Faces)
        {
            faced.UnionWith(face);
            if (face.Distinct().Count(corner => proxy.ClothEnable[corner] != 0f) >= 2)
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
                if (!faced.Contains(v) || node >= feModel.StaticNodeCount)
                {
                    continue;
                }

                if (flexReaches.Contains(v) != feModel.AllowsRotation(node))
                {
                    return false;
                }

                freesAny |= flexReaches.Contains(v);
                continue;
            }

            (string Bone, float Weight)[] influences = feModel.RecoveredSkinWeights.TryGetValue(node, out var recovered) && recovered.Length > 0
                ? [.. recovered]
                : feModel.ResolveSkinBone(node) is { } skinBone ? [(skinBone, 1f)] : [];

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

            if (node >= feModel.StaticNodeCount)
            {
                continue;
            }

            if ((registeredAnchors.Length > 0) != feModel.AllowsRotation(node))
            {
                return false;
            }

            if (!feModel.AllowsRotation(node))
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
                if (!feModel.IsStatic(anchorNode))
                {
                    continue;
                }

                if (feModel.SkeletonBoneParents?.GetValueOrDefault(feModel.CtrlNames[anchorNode]) is { } parent
                    && (!ctrlIndexByName.TryGetValue(parent, out var parentNode)
                        || !feModel.IsPositionDriven(parentNode)))
                {
                    return false;
                }
            }
        }

        return freesAny;
    }

    /// <summary>Whether a control node is a jiggle bone's, which its <c>JiggleBone</c> declares on its own.</summary>
    internal static bool IsDeclaredByItsJiggleBone(FeModel feModel, int node)
        => Array.Exists(feModel.JiggleBones, jiggle => jiggle.Node == node);
}
