using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ValveResourceFormat.SourceGenerators;

/// <summary>
/// Generates the entity I/O input dispatch of every entity class declaring <c>[EntityInput]</c> methods.
/// </summary>
/// <remarks>
/// Each class gets a case-insensitive table of its own inputs and a <c>DispatchInput</c> override that
/// falls back to the base class, so inputs are inherited. <c>BaseEntity</c> declares the virtual root.
/// The class must be a top level, non generic partial class.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EntityInputGenerator : IIncrementalGenerator
{
    private const string EntitiesNamespace = "ValveResourceFormat.Renderer.Entities";
    private const string AttributeName = EntitiesNamespace + ".EntityInputAttribute";
    private const string BaseEntityName = EntitiesNamespace + ".BaseEntity";
    private const string InputDataType = "global::" + EntitiesNamespace + ".EntityInputData";

    private static readonly DiagnosticDescriptor UnsupportedClass = new(
        "VRFINPUT001",
        "Entity input class is not supported",
        "Entity class '{0}' is nested or generic, so its inputs cannot be generated",
        "EntityInput",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateInput = new(
        "VRFINPUT002",
        "Duplicate entity input",
        "Entity class '{0}' declares input '{1}' more than once",
        "EntityInput",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeName,
            static (_, _) => true,
            static (context, _) => GetInput((IMethodSymbol)context.TargetSymbol, context.Attributes[0], context.TargetNode.GetLocation()))
            .Collect();

        context.RegisterSourceOutput(inputs, static (context, inputs) =>
        {
            foreach (var type in inputs.GroupBy(static input => input.TypeFullName))
            {
                var first = type.First();

                if (first.IsUnsupported)
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedClass, first.Location, first.TypeName));
                    continue;
                }

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hasDuplicate = false;

                foreach (var input in type)
                {
                    if (!names.Add(input.InputName))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(DuplicateInput, input.Location, input.TypeName, input.InputName));
                        hasDuplicate = true;
                    }
                }

                if (hasDuplicate)
                {
                    continue;
                }

                var hintName = type.Key.Replace("global::", "") + ".EntityInputs.g.cs";
                context.AddSource(hintName, SourceText.From(Generate(type.ToImmutableArray()), Encoding.UTF8));
            }
        });
    }

    private sealed class Input(string typeFullName, string typeName, string? typeNamespace, bool isRoot, bool isUnsupported, string inputName, string methodName, Location location)
    {
        public string TypeFullName { get; } = typeFullName;
        public string TypeName { get; } = typeName;
        public string? TypeNamespace { get; } = typeNamespace;
        public bool IsRoot { get; } = isRoot;
        public bool IsUnsupported { get; } = isUnsupported;
        public string InputName { get; } = inputName;
        public string MethodName { get; } = methodName;
        public Location Location { get; } = location;
    }

    private static Input GetInput(IMethodSymbol method, AttributeData attribute, Location location)
    {
        var type = method.ContainingType;
        var typeNamespace = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var isUnsupported = type.ContainingType != null || type.IsGenericType;
        var inputName = attribute.ConstructorArguments[0].Value as string ?? string.Empty;

        return new Input(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.Name,
            typeNamespace,
            type.ToDisplayString() == BaseEntityName,
            isUnsupported,
            inputName,
            method.Name,
            location);
    }

    private static string Generate(ImmutableArray<Input> inputs)
    {
        var type = inputs[0];
        var handlerType = FormattableString.Invariant($"global::System.Action<{type.TypeName}, {InputDataType}>");

        var source = new StringBuilder();
        source.Append("// <auto-generated/>\n");
        source.Append("using System.Collections.Frozen;\n\n");

        if (type.TypeNamespace != null)
        {
            source.Append(FormattableString.Invariant($"namespace {type.TypeNamespace};\n\n"));
        }

        source.Append(FormattableString.Invariant($"partial class {type.TypeName}\n"));
        source.Append("{\n");
        source.Append(FormattableString.Invariant($"    private static readonly FrozenDictionary<string, {handlerType}> EntityInputHandlers = new Dictionary<string, {handlerType}>(StringComparer.OrdinalIgnoreCase)\n"));
        source.Append("    {\n");

        foreach (var input in inputs.OrderBy(static input => input.InputName, StringComparer.Ordinal))
        {
            source.Append(FormattableString.Invariant($"        [{SymbolDisplay.FormatLiteral(input.InputName, quote: true)}] = static (entity, data) => entity.{input.MethodName}(data),\n"));
        }

        source.Append("    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);\n\n");

        if (type.IsRoot)
        {
            source.Append("    /// <summary>Runs the handler declared with <see cref=\"EntityInputAttribute\"/> for an input.</summary>\n");
            source.Append("    /// <returns><see langword=\"true\"/> when a handler ran.</returns>\n");
            source.Append(FormattableString.Invariant($"    protected virtual bool DispatchInput(string inputName, {InputDataType} data)\n"));
        }
        else
        {
            source.Append("    /// <inheritdoc/>\n");
            source.Append(FormattableString.Invariant($"    protected override bool DispatchInput(string inputName, {InputDataType} data)\n"));
        }

        var fallback = type.IsRoot ? "false" : "base.DispatchInput(inputName, data)";

        source.Append("    {\n");
        source.Append("        if (EntityInputHandlers.TryGetValue(inputName, out var handler))\n");
        source.Append("        {\n");
        source.Append("            handler(this, data);\n");
        source.Append("            return true;\n");
        source.Append("        }\n\n");
        source.Append(FormattableString.Invariant($"        return {fallback};\n"));
        source.Append("    }\n");
        source.Append("}\n");
        return source.ToString();
    }
}
