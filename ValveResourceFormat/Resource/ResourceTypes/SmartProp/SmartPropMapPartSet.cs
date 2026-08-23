using System.Globalization;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.SmartProps;

/// <summary>One evaluated model part saved for a SmartProp instance in a VMAP document.</summary>
/// <param name="ModelName">The model resource name.</param>
/// <param name="Transform">The part transform relative to the placed SmartProp.</param>
/// <param name="TintColor">The optional part tint.</param>
/// <param name="Deformer">The optional saved deformation cage applied to the part.</param>
public sealed record SmartPropMapPart(
    string ModelName,
    Matrix4x4 Transform,
    Vector4? TintColor,
    SmartPropMapDeformer? Deformer = null);

/// <summary>Represents a saved SmartProp lattice deformer.</summary>
/// <param name="LocalToWorld">Transform from deformer-local space to the placed SmartProp space.</param>
/// <param name="Size">Size of the undeformed lattice.</param>
/// <param name="InterpolationMode">Interpolation used along the primary lattice axis.</param>
/// <param name="ControlPoints">The eight deformed lattice corner positions.</param>
/// <param name="CurveSegmentMidpointPositions">The two curve handles for each of the four longitudinal edges.</param>
public sealed record SmartPropMapDeformer(
    Matrix4x4 LocalToWorld,
    Vector3 Size,
    string InterpolationMode,
    IReadOnlyList<Vector3> ControlPoints,
    IReadOnlyList<Vector3> CurveSegmentMidpointPositions)
{
    private Matrix4x4 WorldToLocal { get; } = Invert(LocalToWorld);

    /// <summary>Maps a position in placed SmartProp space through the saved deformation cage.</summary>
    /// <param name="position">Position in placed SmartProp space.</param>
    /// <returns>The deformed position in placed SmartProp space.</returns>
    public Vector3 DeformPosition(Vector3 position)
    {
        var local = Vector3.Transform(position, WorldToLocal);
        var x = Size.X == 0f ? 0f : local.X / Size.X;
        var y = Size.Y == 0f ? 0f : local.Y / Size.Y;
        var z = Size.Z == 0f ? 0f : local.Z / Size.Z;

        var edge00 = EvaluateEdge(0, 4, 0, x);
        var edge10 = EvaluateEdge(1, 5, 2, x);
        var edge01 = EvaluateEdge(2, 6, 4, x);
        var edge11 = EvaluateEdge(3, 7, 6, x);
        var lower = Vector3.Lerp(edge00, edge10, y);
        var upper = Vector3.Lerp(edge01, edge11, y);
        return Vector3.Transform(Vector3.Lerp(lower, upper, z), LocalToWorld);
    }

    private Vector3 EvaluateEdge(int start, int end, int midpoint, float amount)
        => string.Equals(InterpolationMode, "BEZIER", StringComparison.OrdinalIgnoreCase)
            ? MathUtils.CubicBezier(
                ControlPoints[start],
                CurveSegmentMidpointPositions[midpoint],
                CurveSegmentMidpointPositions[midpoint + 1],
                ControlPoints[end],
                amount)
            : Vector3.Lerp(ControlPoints[start], ControlPoints[end], amount);

    private static Matrix4x4 Invert(Matrix4x4 transform)
        => Matrix4x4.Invert(transform, out var inverse) ? inverse : Matrix4x4.Identity;
}

/// <summary>Reads the evaluated SmartProp model parts cached in a VMAP document.</summary>
public static class SmartPropMapPartSet
{
    /// <summary>Reads saved model parts keyed by their VMAP node id.</summary>
    /// <param name="mapRoot">The VMAP document root.</param>
    /// <returns>Saved evaluated parts keyed by source node id.</returns>
    public static IReadOnlyDictionary<int, IReadOnlyList<SmartPropMapPart>> ReadAll(Datamodel.Element mapRoot)
    {
        Dictionary<int, IReadOnlyList<SmartPropMapPart>> result = [];
        if (!mapRoot.TryGetValue("nodeInstanceData", out var instanceDataValue)
            || instanceDataValue is not Datamodel.ElementArray instanceData)
        {
            return result;
        }

        for (var i = 0; i < instanceData.Count; i++)
        {
            if (instanceData[i] is not Datamodel.Element instance
                || !int.TryParse(instance.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeId)
                || !TryGetPartSet(instance, out var partSet))
            {
                continue;
            }

            var resourceNames = ReadResourceNames(partSet);
            var deformers = ReadDeformers(partSet);
            var parts = ReadParts(partSet, resourceNames, deformers);
            if (parts.Count > 0)
            {
                result[nodeId] = parts;
            }
        }

        return result;
    }

    private static bool TryGetPartSet(Datamodel.Element instance, out Datamodel.Element partSet)
    {
        if (instance.TryGetValue("genericData", out var genericDataValue)
            && genericDataValue is Datamodel.Element genericData
            && genericData.TryGetValue("smartPropPartSet", out var partSetValue)
            && partSetValue is Datamodel.Element value)
        {
            partSet = value;
            return true;
        }

        partSet = null!;
        return false;
    }

    private static List<string> ReadResourceNames(Datamodel.Element partSet)
    {
        List<string> resourceNames = [];
        if (!partSet.TryGetValue("m_ResourceNames", out var namesValue)
            || namesValue is not Datamodel.ElementArray names)
        {
            return resourceNames;
        }

        for (var i = 0; i < names.Count; i++)
        {
            resourceNames.Add(TryUnwrap(names[i], out var value)
                && value.TryGetValue("value", out var nameValue)
                && nameValue is string name
                    ? name
                    : string.Empty);
        }

        return resourceNames;
    }

    private static List<SmartPropMapDeformer> ReadDeformers(Datamodel.Element partSet)
    {
        List<SmartPropMapDeformer> deformers = [];
        if (!partSet.TryGetValue("m_Deformers", out var deformersValue)
            || deformersValue is not Datamodel.ElementArray deformerElements)
        {
            return deformers;
        }

        for (var i = 0; i < deformerElements.Count; i++)
        {
            if (!TryUnwrap(deformerElements[i], out var deformer)
                || ReadVectorArray(deformer, "m_ControlPointPositions") is not { Length: >= 8 } controlPoints
                || ReadVectorArray(deformer, "m_CurveSegmentMidpointPositions") is not { Length: >= 8 } midpoints)
            {
                continue;
            }

            deformers.Add(new SmartPropMapDeformer(
                ReadTransform(deformer),
                ReadVector(deformer, "m_vSize", Vector3.One),
                ReadString(deformer, "m_nInterpolationMode"),
                controlPoints,
                midpoints));
        }

        return deformers;
    }

    private static List<SmartPropMapPart> ReadParts(
        Datamodel.Element partSet,
        List<string> resourceNames,
        List<SmartPropMapDeformer> deformers)
    {
        List<SmartPropMapPart> parts = [];
        if (!partSet.TryGetValue("m_Parts", out var partsValue)
            || partsValue is not Datamodel.ElementArray partElements)
        {
            return parts;
        }

        for (var i = 0; i < partElements.Count; i++)
        {
            if (!TryUnwrap(partElements[i], out var part))
            {
                continue;
            }

            var resourceIndex = ReadInt32(part, "m_nResourceNameIndex", -1);
            if (resourceIndex < 0 || resourceIndex >= resourceNames.Count
                || resourceNames[resourceIndex] is not { Length: > 0 } modelName)
            {
                continue;
            }

            var deformerIndex = ReadInt32(part, "m_nDeformerIndex", -1);
            var deformer = deformerIndex >= 0 && deformerIndex < deformers.Count
                ? deformers[deformerIndex]
                : null;
            parts.Add(new SmartPropMapPart(modelName, ReadTransform(part), ReadTint(part), deformer));
        }

        return parts;
    }

    private static bool TryUnwrap(object? value, out Datamodel.Element element)
    {
        if (value is Datamodel.Element wrapper
            && wrapper.TryGetValue("value", out var elementValue)
            && elementValue is Datamodel.Element nested)
        {
            element = nested;
            return true;
        }

        element = null!;
        return false;
    }

    private static Matrix4x4 ReadTransform(Datamodel.Element part)
    {
        if (!part.TryGetValue("m_Transform", out var transformValue)
            || transformValue is not Datamodel.FloatArray { Count: >= 8 } transform)
        {
            return Matrix4x4.Identity;
        }

        var translation = new Vector3(transform[0], transform[1], transform[2]);
        var uniformScale = transform[3];
        var rotation = Quaternion.Normalize(new Quaternion(transform[4], transform[5], transform[6], transform[7]));
        var nonUniformScale = ReadVector(part, "m_vNonUniformScale", Vector3.One);

        return Matrix4x4.CreateScale(nonUniformScale * uniformScale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
    }

    private static Vector4? ReadTint(Datamodel.Element part)
    {
        if (!part.TryGetValue("m_TintColor", out var tintValue)
            || tintValue is not Datamodel.UInt64Array { Count: >= 3 } tint)
        {
            return null;
        }

        var alpha = tint.Count > 3 ? tint[3] : byte.MaxValue;
        return new Vector4(tint[0], tint[1], tint[2], alpha) / byte.MaxValue;
    }

    private static Vector3 ReadVector(Datamodel.Element element, string name, Vector3 fallback)
        => element.TryGetValue(name, out var value) && value is Datamodel.FloatArray { Count: >= 3 } vector
            ? new Vector3(vector[0], vector[1], vector[2])
            : fallback;

    private static Vector3[]? ReadVectorArray(Datamodel.Element element, string name)
    {
        if (!element.TryGetValue(name, out var value) || value is not Datamodel.Vector3Array vectors)
        {
            return null;
        }

        var result = new Vector3[vectors.Count];
        for (var i = 0; i < vectors.Count; i++)
        {
            result[i] = vectors[i];
        }

        return result;
    }

    private static string ReadString(Datamodel.Element element, string name)
        => element.TryGetValue(name, out var value) && value is string text ? text : string.Empty;

    private static int ReadInt32(Datamodel.Element element, string name, int fallback)
        => element.TryGetValue(name, out var value) && value is IConvertible convertible
            ? convertible.ToInt32(CultureInfo.InvariantCulture)
            : fallback;
}
