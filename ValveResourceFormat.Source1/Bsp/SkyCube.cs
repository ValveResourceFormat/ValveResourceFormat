namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Maps the texels of a cube texture to the six images of a 2D sky, which are named by a suffix such as <c>ft</c>.
/// </summary>
internal static class SkyCube
{
    private readonly record struct SkyFace(string Suffix, Vector3 Forward, Vector3 Right, Vector3 Up);

    private static readonly SkyFace[] SkyFaces =
    [
        new("ft", -Vector3.UnitY, -Vector3.UnitX, Vector3.UnitZ),
        new("bk", Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
        new("lf", -Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
        new("rt", Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ),
        new("up", Vector3.UnitZ, -Vector3.UnitY, -Vector3.UnitX),
        new("dn", -Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitX),
    ];

    /// <summary>The number of sky images, which is also the number of cube faces.</summary>
    public static int FaceCount => SkyFaces.Length;

    /// <summary>Gets the file name suffix of a sky image.</summary>
    public static string GetSuffix(int skyFace) => SkyFaces[skyFace].Suffix;

    /// <summary>
    /// Gets the point of a sky image that each texel of a cube face looks at.
    /// </summary>
    /// <param name="glFace">The cube face, in OpenGL order.</param>
    /// <param name="size">The width and height of the cube face in texels.</param>
    /// <param name="skyFace">The sky image all texels of the cube face look at.</param>
    /// <returns>The coordinates from 0 to 1 in the sky image of every texel of the cube face, row by row.</returns>
    public static Vector2[] GetFaceTexCoords(int glFace, int size, out int skyFace)
    {
        // Every texel of a cube face looks furthest along the face's own axis, so one sky image fills all of it
        skyFace = FindSkyFace(GetCubeDirection(glFace, 0f, 0f));

        var face = SkyFaces[skyFace];
        var texCoords = new Vector2[size * size];

        for (var y = 0; y < size; y++)
        {
            var v = (2f * (y + 0.5f) / size) - 1f;

            for (var x = 0; x < size; x++)
            {
                var u = (2f * (x + 0.5f) / size) - 1f;
                var direction = GetCubeDirection(glFace, u, v);
                var forward = Vector3.Dot(direction, face.Forward);
                var s = Vector3.Dot(direction, face.Right) / forward;
                var t = Vector3.Dot(direction, face.Up) / forward;

                texCoords[(y * size) + x] = new Vector2((s + 1f) * 0.5f, (1f - t) * 0.5f);
            }
        }

        return texCoords;
    }

    private static Vector3 GetCubeDirection(int glFace, float u, float v) => glFace switch
    {
        0 => new Vector3(1f, -v, -u),
        1 => new Vector3(-1f, -v, u),
        2 => new Vector3(u, 1f, v),
        3 => new Vector3(u, -1f, -v),
        4 => new Vector3(u, -v, 1f),
        _ => new Vector3(-u, -v, -1f),
    };

    private static int FindSkyFace(Vector3 direction)
    {
        var best = 0;
        var bestDot = float.MinValue;

        for (var i = 0; i < SkyFaces.Length; i++)
        {
            var dot = Vector3.Dot(direction, SkyFaces[i].Forward);

            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        return best;
    }
}
