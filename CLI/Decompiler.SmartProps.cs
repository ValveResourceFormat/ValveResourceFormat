using ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Utils;

namespace CLI
{
    public partial class Decompiler
    {
        private int? SmartPropSeed;
        private bool HasSmartPropOptions => SmartPropSeed != null;

        /// <summary>
        /// Prints what a smart prop evaluates to with <see cref="SmartPropSeed"/> as the master seed and every parameter
        /// at its default. Nested smart props load from the game the file belongs to.
        /// </summary>
        private void EvaluateSmartProp(SmartProp smartProp, string path, IFileLoader? fileLoader, Package? package)
        {
            using var gameFileLoader = fileLoader == null ? CreateGameFileLoader(package, path) : null;

            var evaluator = new SmartPropEvaluator(fileLoader ?? gameFileLoader);
            var output = evaluator.Evaluate(SmartPropDefinition.Load(smartProp), new SmartPropEvaluationInput { MasterSeed = SmartPropSeed ?? 0 });

            using var writer = new IndentedTextWriter();
            output.WriteText(writer);
            Stdout.Write(writer.ToString());
        }
    }
}
