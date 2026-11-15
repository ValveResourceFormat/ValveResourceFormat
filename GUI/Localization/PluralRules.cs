#nullable enable

namespace GUI.Utils;

/// <summary>CLDR plural categories.</summary>
internal enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

/// <summary>
/// Cardinal plural rules of the shipped languages, one <c>Select</c> method per language code.
/// This file is also compiled into the source generator, which checks translations against <see cref="GetCategories"/>.
/// </summary>
// TODO: add the rules of further languages from CLDR plurals.xml as translations arrive
internal static class PluralRules
{
    /// <summary>Returns the categories a language uses, or null when it has no rules here.</summary>
    public static PluralCategory[]? GetCategories(string language) => language switch
    {
        "en" => [PluralCategory.One, PluralCategory.Other],
        "ru" => [PluralCategory.One, PluralCategory.Few, PluralCategory.Many, PluralCategory.Other],
        _ => null,
    };

    /// <summary>Returns the category of an integer count in English.</summary>
    public static PluralCategory SelectEn(int count)
    {
        var i = count < 0 ? -(long)count : count;
        return i == 1 ? PluralCategory.One : PluralCategory.Other;
    }

    /// <summary>Returns the category of an integer count in Russian.</summary>
    public static PluralCategory SelectRu(int count)
    {
        var i = count < 0 ? -(long)count : count;

        if (i % 10 == 1 && i % 100 != 11)
        {
            return PluralCategory.One;
        }

        if (i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14))
        {
            return PluralCategory.Few;
        }

        if (i % 10 == 0 || i % 10 is >= 5 and <= 9 || i % 100 is >= 11 and <= 14)
        {
            return PluralCategory.Many;
        }

        return PluralCategory.Other;
    }
}
