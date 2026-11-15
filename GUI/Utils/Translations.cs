using System.Globalization;

namespace GUI.Utils;

/// <summary>Selects the language of the generated texts, read through <see cref="Locale"/>.</summary>
internal static partial class Translations
{
    private const string LanguageEnvironmentVariable = "SOURCE2VIEWER_LANGUAGE";

    // Swapped as one object so a reader never sees a mix of two languages
    private static volatile English current = new();

    /// <summary>The texts of the selected language.</summary>
    public static English Locale => current;

    /// <summary>The UI culture of the system, read before the process switches to the invariant culture.</summary>
    public static CultureInfo SystemCulture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Selects the language of <see cref="Settings.AppConfig.Language"/>, falling back to the system language and then English.
    /// </summary>
    public static void Load()
    {
        // Automation finds controls by their English text
        Select(Automation.Automation.IsEnabled ? CultureInfo.InvariantCulture : GetRequestedCulture());
    }

    // Switch to the translation of a culture, or to English when there is none
    private static void Select(CultureInfo culture)
    {
        // A regional culture such as pt-BR uses the translation of its parent when it has none of its own
        for (; culture.Name.Length > 0; culture = culture.Parent)
        {
            if (CreateLanguage(culture.Name) is { } language)
            {
                current = language;
                return;
            }
        }

        current = new English();
    }

    private static CultureInfo GetRequestedCulture()
    {
        var name = Settings.Config.Language;

        if (name.Length == 0)
        {
            name = Environment.GetEnvironmentVariable(LanguageEnvironmentVariable) ?? string.Empty;
        }

        if (name.Length == 0)
        {
            return SystemCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            Log.Warn(nameof(Translations), $"Unknown language '{name}'");
            return CultureInfo.InvariantCulture;
        }
    }
}
