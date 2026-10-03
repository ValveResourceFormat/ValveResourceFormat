using System.Linq;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>Gets the proxy surface a painted render vertex is bound to, or null when the model has no proxy face.</summary>
    internal ClothProxySurface? RenderBindingSurface { get; private set; }

    private ClothProxySurface? BuildRenderBindingSurface()
    {
        if (model is null || physAggregateData?.FeModel is not { } cloth)
        {
            return null;
        }

        var boneNames = model.Skeleton.Bones.Select(static bone => bone.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var faces = new List<ClothProxySurface.Face>();

        foreach (var proxyFile in ProxyMeshes)
        {
            var proxy = proxyFile.Proxy;

            foreach (var face in proxy.Faces)
            {
                if (face.Length is < 3 or > 4)
                {
                    continue;
                }

                var corners = new Vector3[face.Length];
                var names = new string[face.Length];
                var ownsBone = new bool[face.Length];
                var boneless = new bool[face.Length];

                for (var i = 0; i < face.Length; i++)
                {
                    var node = proxy.NodeIndices[face[i]];
                    corners[i] = proxy.Positions[face[i]];
                    names[i] = cloth.CtrlNames[node];

                    var generated = cloth.AllowsRotation(node) && ClothBones.IsProxyName(names[i]);
                    ownsBone[i] = generated && boneNames.Contains(names[i]);
                    boneless[i] = generated && !ownsBone[i];
                }

                faces.Add(new ClothProxySurface.Face(corners, names, ownsBone, boneless));
            }
        }

        return faces.Count > 0 ? new ClothProxySurface(faces) : null;
    }
}
