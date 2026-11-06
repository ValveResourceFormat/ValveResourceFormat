namespace ValveResourceFormat.Source1.Model;

internal static class ModelNames
{
    /// <summary>
    /// Replaces the characters that the model compiler rejects in the name of a mesh or an animation.
    /// </summary>
    public static string Sanitize(string name)
    {
        var chars = name.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(chars[i]) && chars[i] != '_')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
