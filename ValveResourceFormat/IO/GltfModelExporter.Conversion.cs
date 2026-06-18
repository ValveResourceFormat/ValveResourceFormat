using System;
using System.Numerics;

namespace ValveResourceFormat.IO;

public partial class GltfModelExporter
{
    // NOTE: Swaps Y and Z axes - gltf up axis is Y (source engine up is Z)
    // Also scales inches to meters (gltf units are meters, source engine units are inches)
    // https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#coordinate-system-and-units
    private const float SourceToGltfScale = 0.0254f;
    private static readonly Quaternion SourceToGltfRotation = Quaternion.CreateFromYawPitchRoll(0, MathF.PI / -2f, MathF.PI / -2f);
    private static readonly Matrix4x4 TransformSourceToGltf = Matrix4x4.CreateScale(SourceToGltfScale) * Matrix4x4.CreateFromQuaternion(SourceToGltfRotation);

    // The conversion is baked into the exported geometry (bones, vertices, animation tracks) so the armature
    // is identity-scaled and the bind matrices are clean inverses. The scale folds into translations (a bone
    // has no rest scale); the rotation bakes into the skeleton roots.

    /// <summary>
    /// Applies the conversion to a bone-local transform: scales the translation, and on skeleton roots also
    /// applies the axis rotation (children inherit it through the hierarchy). Used for both bone rest poses
    /// and animation keyframes.
    /// </summary>
    private static (Vector3 Translation, Quaternion Rotation) BakeConversion(Vector3 translation, Quaternion rotation, bool isRoot)
    {
        translation *= SourceToGltfScale;
        if (isRoot)
        {
            translation = Vector3.Transform(translation, SourceToGltfRotation);
            rotation = SourceToGltfRotation * rotation;
        }

        return (translation, rotation);
    }

    /// <summary>
    /// The node transform for placed geometry. The conversion is already baked into the geometry, so a
    /// standalone model resolves to identity; a world/entity placement is conjugated by the conversion so it
    /// positions the converted geometry correctly.
    /// </summary>
    private static Matrix4x4 GetPlacementTransform(Matrix4x4 transform)
    {
        Matrix4x4.Invert(TransformSourceToGltf, out var gltfToSource);
        return gltfToSource * transform * TransformSourceToGltf;
    }

    private static void BakePositions(Span<Vector3> positions)
    {
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = Vector3.Transform(positions[i], TransformSourceToGltf);
        }
    }

    private static void BakeDirections(Span<Vector3> directions)
    {
        for (var i = 0; i < directions.Length; i++)
        {
            directions[i] = Vector3.Transform(directions[i], SourceToGltfRotation);
        }
    }

    private static void BakeTangents(Span<Vector4> tangents)
    {
        for (var i = 0; i < tangents.Length; i++)
        {
            var rotated = Vector3.Transform(new Vector3(tangents[i].X, tangents[i].Y, tangents[i].Z), SourceToGltfRotation);
            tangents[i] = new Vector4(rotated, tangents[i].W);
        }
    }
}
