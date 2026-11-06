using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// The materials that carry what a model tells its eye and teeth shaders: the eyeball it draws, or the mouth that lights it.
/// Each is a copy of a material of the game with a name of its own, which the model converter registers and the resource
/// provider reads the values back from.
/// </summary>
internal sealed class Source1ModelMaterials
{
    /// <summary>The variant name of a material of a model whose ambient light is boosted.</summary>
    public const string AmbientBoostVariant = "ambientboost";

    private const string EyeballSuffix = "_eyeball";
    private const string MouthSuffix = "_mouth";

    /// <summary>The material of an eyeball.</summary>
    /// <param name="Material">The path of the material the eyeball is drawn with, such as <c>materials/models/eyeball_r.vmat</c>.</param>
    /// <param name="Eyeball">The eyeball, in the space the converted vertices are in.</param>
    /// <param name="TargetBone">The bone of the view target of the eyes.</param>
    /// <param name="TargetPosition">The position of the view target of the eyes.</param>
    public sealed record EyeballMaterial(string Material, Source1Eyeball Eyeball, int TargetBone, Vector3 TargetPosition);

    /// <summary>The material of the teeth of a mouth.</summary>
    /// <param name="Material">The path of the material the teeth are drawn with, such as <c>materials/models/teeth.vmat</c>.</param>
    /// <param name="Bone">The bone the mouth is attached to.</param>
    /// <param name="Forward">The direction the mouth points in, in the space the converted vertices are in.</param>
    public sealed record MouthMaterial(string Material, int Bone, Vector3 Forward);

    private readonly ConcurrentDictionary<string, EyeballMaterial> eyeballs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MouthMaterial> mouths = new(StringComparer.OrdinalIgnoreCase);

    // The path a material was given, so that the many meshes and skins that share an eyeball or mouth hash it once
    private readonly ConcurrentDictionary<(string Material, Source1Eyeball Eyeball, int TargetBone, Vector3 TargetPosition), string> eyeballPaths = [];
    private readonly ConcurrentDictionary<(string Material, int Bone, Vector3 Forward), string> mouthPaths = [];

    /// <summary>Registers the material of an eyeball.</summary>
    /// <param name="material">The path of the material the eyeball is drawn with.</param>
    /// <param name="eyeball">The eyeball, in the space the converted vertices are in.</param>
    /// <param name="target">The view target of the eyes.</param>
    /// <returns>The path the material is served by.</returns>
    public string AddEyeball(string material, Source1Eyeball eyeball, Source1EyeTarget target)
    {
        var key = (material, eyeball, target.Index, target.Position);

        if (eyeballPaths.TryGetValue(key, out var known))
        {
            return known;
        }

        Span<float> values =
        [
            eyeball.Bone,
            eyeball.Origin.X, eyeball.Origin.Y, eyeball.Origin.Z,
            eyeball.Up.X, eyeball.Up.Y, eyeball.Up.Z,
            eyeball.Forward.X, eyeball.Forward.Y, eyeball.Forward.Z,
            eyeball.ZOffset,
            eyeball.IrisScale,
            target.Index,
            target.Position.X, target.Position.Y, target.Position.Z,
        ];

        var path = GetPath(material, EyeballSuffix, values);
        eyeballs.TryAdd(path, new EyeballMaterial(material, eyeball, target.Index, target.Position));
        eyeballPaths[key] = path;

        return path;
    }

    /// <summary>Registers the material of the teeth of a mouth.</summary>
    /// <param name="material">The path of the material the teeth are drawn with.</param>
    /// <param name="mouth">The mouth, with its direction in the space the converted vertices are in.</param>
    /// <returns>The path the material is served by.</returns>
    public string AddMouth(string material, Source1Mouth mouth)
    {
        var key = (material, mouth.Bone, mouth.Forward);

        if (mouthPaths.TryGetValue(key, out var known))
        {
            return known;
        }

        Span<float> values = [mouth.Bone, mouth.Forward.X, mouth.Forward.Y, mouth.Forward.Z];

        var path = GetPath(material, MouthSuffix, values);
        mouths.TryAdd(path, new MouthMaterial(material, mouth.Bone, mouth.Forward));
        mouthPaths[key] = path;

        return path;
    }

    /// <summary>Gets whether a path was made by <see cref="AddEyeball"/> or <see cref="AddMouth"/>.</summary>
    public bool Contains(string path)
    {
        if (path.Contains(EyeballSuffix, StringComparison.OrdinalIgnoreCase) && eyeballs.ContainsKey(path))
        {
            return true;
        }

        return path.Contains(MouthSuffix, StringComparison.OrdinalIgnoreCase) && mouths.ContainsKey(path);
    }

    /// <summary>Gets the eyeball material a path made by <see cref="AddEyeball"/> names.</summary>
    public bool TryGetEyeball(string path, [NotNullWhen(true)] out EyeballMaterial? eyeball) => eyeballs.TryGetValue(path, out eyeball);

    /// <summary>Gets the mouth material a path made by <see cref="AddMouth"/> names.</summary>
    public bool TryGetMouth(string path, [NotNullWhen(true)] out MouthMaterial? mouth) => mouths.TryGetValue(path, out mouth);

    // The name holds a hash of the values, so a model gets the same names whatever was converted before it
    private static string GetPath(string material, string suffix, ReadOnlySpan<float> values)
    {
        var hash = XxHash32.HashToUInt32(MemoryMarshal.AsBytes(values));
        var extension = Path.GetExtension(material);
        var stem = material[..^extension.Length];

        return string.Create(CultureInfo.InvariantCulture, $"{stem}{suffix}{hash:x8}{extension}");
    }
}
