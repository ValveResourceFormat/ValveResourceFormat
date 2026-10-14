namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// Per-vertex maps of the attributes that <see cref="VBIB.TransformVertexAttributes"/> rewrites.
    /// </summary>
    /// <param name="Position">Maps a source position to its new position.</param>
    /// <param name="Normal">Maps a unit normal, given the source position of its vertex, to a unit normal.</param>
    /// <param name="Tangent">Maps a unit tangent, given the source position of its vertex, to a unit tangent.</param>
    /// <param name="Mirrored">Whether the map mirrors the geometry, which flips bitangent signs and triangle winding.</param>
    public sealed record VertexAttributeMap(
        Func<Vector3, Vector3> Position,
        Func<Vector3, Vector3, Vector3> Normal,
        Func<Vector3, Vector3, Vector3> Tangent,
        bool Mirrored = false)
    {
        /// <summary>
        /// The map of a linear transform: normals by its inverse transpose, tangents by the transform itself, mirrored
        /// when its determinant is negative.
        /// </summary>
        /// <param name="matrix">The transform, in the row-vector convention.</param>
        /// <returns>The map.</returns>
        public static VertexAttributeMap FromMatrix(Matrix4x4 matrix)
        {
            var normalMatrix = Matrix4x4.Invert(matrix, out var inverse) ? Matrix4x4.Transpose(inverse) : matrix;

            return new VertexAttributeMap(
                position => Vector3.Transform(position, matrix),
                (_, normal) => MathUtils.SafeNormalize(Vector3.TransformNormal(normal, normalMatrix)),
                (_, tangent) => MathUtils.SafeNormalize(Vector3.TransformNormal(tangent, matrix)),
                matrix.GetDeterminant() < 0f);
        }
    }
}
