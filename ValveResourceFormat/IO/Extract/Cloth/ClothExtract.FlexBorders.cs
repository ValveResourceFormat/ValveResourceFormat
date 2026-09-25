using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// Whether every pin <c>flex_cloth_borders</c> would free on <paramref name="proxy"/> (a static corner of a face
    /// joined to two or more simulated corners) carries an <c>m_NodeBases</c> entry in the original. On a sheet that
    /// adds bones to the render mesh the flag gives each such pin a node base, while the per-vertex
    /// <c>cloth_anchor_free_rotate</c> paint frees it without one.
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
    /// Whether the original's own node bases state <c>flex_cloth_borders</c> on <paramref name="proxy"/>. A proxy
    /// vertex is a virtual node, so the compiler admits a PINNED one to <c>m_NodeBases</c> only through the border
    /// pass, which writes the basis flag on a freed pin only when <c>add_bones_to_render_mesh</c> is set as well.
    /// The per-vertex <c>cloth_anchor_free_rotate</c> paint frees the same pin without a basis, so an entry on one
    /// of the pins the flag would reach is a witness no other authoring produces.
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
    /// Whether <paramref name="proxy"/> states <c>flex_cloth_borders</c>. A pinned border vertex keeps its rotation
    /// locked unless the sheet was imported with the flag on, so it is re-emitted wherever that reproduces the
    /// original. On a non-back-solving sheet the flag reaches exactly the pins a face joins to two or more simulated
    /// corners: those it frees and gives a node base, which no per-vertex paint does, while a pin every face leaves
    /// with fewer simulated corners stays rotation-locked either way and carries no evidence. The flag is taken when
    /// every reached pin is recorded rotation-free and every unreached one rotation-locked, so the paint the flag
    /// replaces has nothing left to say. On a sheet that adds bones to the render mesh it also needs every reached
    /// pin to carry a node base, since the flag gives it one and the paint frees it without. A back-solving sheet
    /// instead frees exactly the pins with a skin influence on a registered control and its fit machinery pulls
    /// anchor parent chains in, so each pin's influence registration has to match its rot-lock class, no gap slot's
    /// influences may register it (a new node the original does not have), and every freed pin's static anchor needs
    /// its skeleton parent already position-driven - except where the pins' own node bases state the flag outright,
    /// which no other authoring produces and which the anchor chain therefore has nothing to add to.
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

            // A non-back-solving sheet never registers a padded gap slot, so only the
            // recorded rot-lock classes have to agree with the flag's reach.
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

    /// <summary>
    /// Gets whether a control node is a jiggle bone's, which its <c>JiggleBone</c> declares on its own. That node
    /// compiles into the vertex set with no name, and declaring it as a cloth node too moves it into the model's
    /// default set.
    /// </summary>
    internal static bool IsDeclaredByItsJiggleBone(FeModel feModel, int node)
        => Array.Exists(feModel.JiggleBones, jiggle => jiggle.Node == node);
}
