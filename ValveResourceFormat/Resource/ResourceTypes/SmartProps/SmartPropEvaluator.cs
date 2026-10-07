using System.Collections.Concurrent;
using ValveResourceFormat.IO;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Evaluates smart props (<c>.vsmart</c>) into placed models the way the Source 2 smart prop system does.
    /// Nested smart props are loaded through the file loader and cached.
    /// </summary>
    /// <param name="fileLoader">Loader for nested smart props; without one, nested smart props are skipped.</param>
    public sealed class SmartPropEvaluator(IFileLoader? fileLoader)
    {
        private readonly ConcurrentDictionary<string, SmartPropDefinition?> definitions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Evaluates a smart prop.
        /// </summary>
        /// <param name="definition">The smart prop.</param>
        /// <param name="input">Placement, overrides and stored state.</param>
        /// <returns>The placed models and everything they reference.</returns>
        public SmartPropOutput Evaluate(SmartPropDefinition definition, SmartPropEvaluationInput input)
        {
            var context = new SmartPropContext(this, input);
            context.Run(definition);
            return context.Output;
        }

        /// <summary>
        /// Loads a smart prop definition by resource name, cached for the lifetime of the evaluator.
        /// </summary>
        /// <param name="name">Resource name of the <c>.vsmart</c>.</param>
        /// <returns>The definition, or null when it cannot be loaded.</returns>
        public SmartPropDefinition? LoadDefinition(string name)
        {
            if (fileLoader == null)
            {
                return null;
            }

            return definitions.GetOrAdd(name, static (key, loader) =>
            {
                using var resource = loader.LoadFileCompiled(key);
                return resource?.DataBlock is SmartProp smartProp ? SmartPropDefinition.Load(smartProp) : null;
            }, fileLoader);
        }
    }
}
