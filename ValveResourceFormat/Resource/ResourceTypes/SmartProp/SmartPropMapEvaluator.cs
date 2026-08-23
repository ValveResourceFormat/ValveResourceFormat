using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps;

/// <summary>
/// Evaluates the smart props placed in a VMAP document.
/// </summary>
public static class SmartPropMapEvaluator
{
    /// <summary>
    /// Evaluates each placed smart prop whose source document can be resolved.
    /// </summary>
    public static IReadOnlyList<SmartPropMapEvaluation> EvaluateAll(Datamodel.Element mapRoot, Func<string, KVObject?> smartPropResolver)
    {
        List<SmartPropMapEvaluation> evaluations = [];
        foreach (var parameters in SmartPropMapParameters.ReadAll(mapRoot))
        {
            var smartPropRoot = smartPropResolver(parameters.SmartPropFilename);
            if (smartPropRoot != null)
            {
                var context = parameters.CreateEvaluationContext(smartPropRoot);
                evaluations.Add(new SmartPropMapEvaluation(parameters, SmartPropEvaluator.Evaluate(smartPropRoot, context, smartPropResolver)));
            }
        }

        return evaluations;
    }
}

/// <summary>
/// The evaluated result for one smart prop placed in a VMAP document.
/// </summary>
public sealed record SmartPropMapEvaluation(SmartPropMapParameters Parameters, SmartPropEvaluationResult Result);
