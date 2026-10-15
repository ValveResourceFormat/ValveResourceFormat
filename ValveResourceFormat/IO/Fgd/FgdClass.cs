using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO.Fgd;

/// <summary>
/// The kind of class an FGD file declares, by the number the editor knows it under. Each name is the section
/// a class of that kind is declared in, read without regard to case, so renaming one changes what is read.
/// </summary>
public enum FgdClassType
{
    /// <summary><c>@BaseClass</c>, which other classes are based on and which cannot be placed.</summary>
    BaseClass = 1,

    /// <summary><c>@Struct</c></summary>
    Struct = 2,

    /// <summary><c>@OverrideClass</c>, which adds to a class declared before it.</summary>
    OverrideClass = 3,

    /// <summary><c>@PointClass</c></summary>
    PointClass = 4,

    /// <summary><c>@SolidClass</c>, an entity made of map geometry.</summary>
    SolidClass = 5,

    /// <summary><c>@NPCClass</c></summary>
    NpcClass = 6,

    /// <summary><c>@FilterClass</c></summary>
    FilterClass = 7,

    /// <summary><c>@MoveClass</c></summary>
    MoveClass = 8,

    /// <summary><c>@KeyFrameClass</c></summary>
    KeyFrameClass = 9,

    /// <summary><c>@PathClass</c></summary>
    PathClass = 10,

    /// <summary><c>@PathNodeClass</c></summary>
    PathNodeClass = 11,

    /// <summary><c>@CableClass</c></summary>
    CableClass = 12,

    /// <summary><c>@NavLinkClass</c></summary>
    NavLinkClass = 13,

    /// <summary><c>@ChoreoAnchorClass</c></summary>
    ChoreoAnchorClass = 14,

    /// <summary><c>@ModelGameData</c></summary>
    ModelGameData = 15,

    /// <summary><c>@ModelBreakCommand</c></summary>
    ModelBreakCommand = 16,

    /// <summary><c>@ModelAnimEvent</c></summary>
    ModelAnimEvent = 17,

    /// <summary><c>@VData</c></summary>
    VData = 18,

    /// <summary><c>@VDataDerived</c></summary>
    VDataDerived = 19,
}

/// <summary>
/// The type of the value an input takes or an output passes on.
/// </summary>
public enum FgdInputOutputType
{
    /// <summary><c>void</c></summary>
    Void = 0,

    /// <summary><c>integer</c></summary>
    Integer = 1,

    /// <summary><c>bool</c> or <c>boolean</c></summary>
    Boolean = 2,

    /// <summary><c>string</c></summary>
    String = 3,

    /// <summary><c>float</c></summary>
    Float = 4,

    /// <summary><c>vector</c></summary>
    Vector = 5,

    /// <summary><c>target_destination</c> or <c>ehandle</c></summary>
    TargetDestination = 6,

    /// <summary><c>color255</c></summary>
    Color255 = 7,

    /// <summary><c>script</c></summary>
    Script = 8,
}

/// <summary>
/// A helper a class is drawn with in the editor, as the class declares it.
/// </summary>
public sealed class FgdHelper
{
    /// <summary>Gets the name the helper is created by.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the arguments of the helper, in order.</summary>
    public List<string> Arguments { get; internal set; } = [];

    /// <summary>Gets the keys of a helper declared with a block of them rather than arguments.</summary>
    public KVObject? Metadata { get; internal set; }

    /// <summary>Gets the block of keys as it is written, braces included.</summary>
    public string? MetadataText { get; internal set; }
}

/// <summary>
/// One of the values a variable with a list of them can take.
/// </summary>
public sealed class FgdChoice
{
    /// <summary>Gets the value as a number. Only the bits of a flags variable have one.</summary>
    public int Value { get; init; }

    /// <summary>Gets the value as it is stored in an entity.</summary>
    public string ValueText { get; init; } = string.Empty;

    /// <summary>Gets the name the value is shown under.</summary>
    public string Caption { get; init; } = string.Empty;

    /// <summary>Gets the longer text some lists give each value.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets whether the value is on by default, for flags and tag lists.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// A variable of a class: a key an entity of the class can have.
/// </summary>
public sealed class FgdVariable
{
    /// <summary>Gets the name of the key.</summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>Gets the name the variable is shown under.</summary>
    public string LongName { get; internal set; } = string.Empty;

    /// <summary>Gets the description. A variable declared without a long name is described by its name.</summary>
    public string Description { get; internal set; } = string.Empty;

    /// <summary>Gets the type as it is written between the parentheses.</summary>
    public string TypeName { get; internal set; } = string.Empty;

    /// <summary>Gets the type the variable is declared with.</summary>
    public FgdVariableType DeclaredType { get; internal set; } = FgdVariableType.Invalid;

    /// <summary>Gets the types a type made of other types is made of, outermost first.</summary>
    public List<FgdVariableType> ElementTypes { get; } = [];

    /// <summary>Gets the text that narrows the type down, such as the kind of resource.</summary>
    public string SubType { get; internal set; } = string.Empty;

    /// <summary>Gets the type of the values: the innermost of <see cref="ElementTypes"/>, else <see cref="DeclaredType"/>.</summary>
    public FgdVariableType Type => ElementTypes.Count > 0 ? ElementTypes[^1] : DeclaredType;

    /// <summary>Gets whether the variable is declared with a default.</summary>
    public bool HasDefault { get; internal set; }

    /// <summary>Gets the default of a variable whose values are text.</summary>
    public string DefaultString { get; internal set; } = string.Empty;

    /// <summary>Gets the default of a variable whose values are whole numbers.</summary>
    public int DefaultInteger { get; internal set; }

    /// <summary>Gets the values of a variable that has a list of them.</summary>
    public List<FgdChoice> Choices { get; internal set; } = [];

    /// <summary>Gets what else is declared of the variable, such as <c>report</c> or <c>group</c>.</summary>
    public KVObject Metadata { get; internal set; } = new();

    internal FgdVariable Clone()
    {
        var clone = (FgdVariable)MemberwiseClone();
        clone.Choices = [.. Choices];
        return clone;
    }
}

/// <summary>
/// An input or an output of a class.
/// </summary>
public sealed class FgdInputOutput
{
    /// <summary>Gets the name.</summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>Gets the type of the value that goes with it.</summary>
    public FgdInputOutputType Type { get; internal set; }

    /// <summary>Gets whether it is declared as <c>api</c>, which the game declares the type of.</summary>
    public bool IsApi { get; internal set; }

    /// <summary>Gets the description.</summary>
    public string Description { get; internal set; } = string.Empty;

    /// <summary>Gets what else is declared of it, with the class, file and line it comes from.</summary>
    public KVObject Metadata { get; internal set; } = new();

    internal void CopyFrom(FgdInputOutput other)
    {
        Name = other.Name;
        Type = other.Type;
        IsApi = other.IsApi;
        Description = other.Description;
        Metadata = other.Metadata;
    }
}

/// <summary>
/// A class of an FGD file: an entity class, or one of the other kinds of class the tools declare
/// the same way.
/// </summary>
public sealed class FgdClass
{
    /// <summary>Color of a class that declares none and is based on no class.</summary>
    public static readonly Color32 DefaultColor = new(220, 30, 220);

    /// <summary>Gets the name of the class.</summary>
    public string Name { get; internal set; } = "<unnamed>";

    /// <summary>Gets the description.</summary>
    public string Description { get; internal set; } = string.Empty;

    /// <summary>Gets the kind of class.</summary>
    public FgdClassType Type { get; internal set; }

    /// <summary>Gets the color entities of the class are drawn in.</summary>
    public Color32 Color { get; internal set; } = DefaultColor;

    /// <summary>Gets whether the class declares a color, or took one from a class it is based on.</summary>
    public bool HasColor { get; internal set; }

    /// <summary>Gets the smallest corner of the box entities of the class are drawn as.</summary>
    public Vector3 Mins { get; internal set; } = new(-8f);

    /// <summary>Gets the largest corner of the box entities of the class are drawn as.</summary>
    public Vector3 Maxs { get; internal set; } = new(8f);

    /// <summary>Gets whether the class declares a size, or took one from a class it is based on.</summary>
    public bool HasSize { get; internal set; }

    /// <summary>Gets whether entities of the class snap to half the grid.</summary>
    public bool HalfGridSnap { get; internal set; }

    /// <summary>Gets the classes the class is based on, in the order it names them.</summary>
    public List<FgdClass> BaseClasses { get; } = [];

    /// <summary>
    /// Gets the helpers the class declares, in order. They are not taken from the classes it is based on.
    /// </summary>
    public List<FgdHelper> Helpers { get; } = [];

    /// <summary>Gets the tags of the class.</summary>
    public List<string> Tags { get; } = [];

    /// <summary>Gets the keys declared for the class as a whole.</summary>
    public KVObject Metadata { get; internal set; } = new();

    /// <summary>Gets the variables of the class. Once resolved, the ones of the classes it is based on come first.</summary>
    public List<FgdVariable> Variables { get; internal set; } = [];

    /// <summary>Gets the inputs of the class.</summary>
    public List<FgdInputOutput> Inputs { get; internal set; } = [];

    /// <summary>Gets the outputs of the class.</summary>
    public List<FgdInputOutput> Outputs { get; internal set; } = [];

    /// <summary>Finds a variable of the class by its name, without regard to case.</summary>
    /// <param name="name">Name of the key.</param>
    /// <returns>The variable, or <see langword="null"/> when the class has none of that name.</returns>
    public FgdVariable? FindVariable(string name)
        => Variables.Find(variable => variable.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal void AddTag(string tag)
    {
        if (tag.Length > 0 && !Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            Tags.Add(tag);
        }
    }

    /// <summary>
    /// Adds a variable the way the editor does, where the class may have one of that name already.
    /// </summary>
    internal void AddVariable(FgdVariable variable)
    {
        var index = Variables.FindIndex(other => other.Name.Equals(variable.Name, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            Variables.Add(variable);
            return;
        }

        var existing = Variables[index];

        if (variable.DeclaredType == FgdVariableType.RemoveKey)
        {
            Variables.RemoveAt(index);
            return;
        }

        // A list of values is added to rather than replaced
        if (existing.DeclaredType == variable.DeclaredType && HasChoices(variable.DeclaredType))
        {
            variable = variable.Clone();
            MergeChoices(variable, existing);
        }

        Variables[index] = variable;
    }

    internal static bool HasChoices(FgdVariableType type) => type
        is FgdVariableType.Choices
        or FgdVariableType.IntChoices
        or FgdVariableType.FloatChoices
        or FgdVariableType.Flags
        or FgdVariableType.TagList
        or FgdVariableType.TagListDynamic
        or FgdVariableType.Suggestions
        or FgdVariableType.FlagChoices;

    // The values the variable does not have yet are taken from the other one
    private static void MergeChoices(FgdVariable variable, FgdVariable other)
    {
        var byText = variable.DeclaredType
            is FgdVariableType.TagList
            or FgdVariableType.TagListDynamic
            or FgdVariableType.Suggestions;

        foreach (var choice in other.Choices)
        {
            var exists = byText
                ? variable.Choices.Exists(own => own.ValueText.Equals(choice.ValueText, StringComparison.OrdinalIgnoreCase))
                : variable.Choices.Exists(own => own.Value == choice.Value);

            if (!exists)
            {
                variable.Choices.Add(choice);
            }
        }
    }
}

/// <summary>
/// A group entity classes are listed under in the editor.
/// </summary>
/// <param name="Name">Name of the group.</param>
/// <param name="StartExpanded">Whether the group is shown open.</param>
public sealed record FgdEntityGroup(string Name, bool StartExpanded);

/// <summary>
/// Something an FGD file declares that could not be read.
/// </summary>
/// <param name="FileName">The file.</param>
/// <param name="Line">The line it was noticed on, counted from one.</param>
/// <param name="Message">What is wrong.</param>
public sealed record FgdError(string FileName, int Line, string Message);
