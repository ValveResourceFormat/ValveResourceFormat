using System.Runtime.CompilerServices;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// A value worked out once per model. Values worked out without a file loader are kept apart,
    /// since only a loader reaches the files the model references.
    /// </summary>
    /// <param name="create">Works the value out for a model.</param>
    internal sealed class ModelCache<T>(Func<Model, IFileLoader?, T> create)
    {
        private readonly ConditionalWeakTable<Model, StrongBox<T>> withLoader = [];
        private readonly ConditionalWeakTable<Model, StrongBox<T>> withoutLoader = [];

        /// <summary>The value for <paramref name="model"/>, worked out on the first request.</summary>
        public T Get(Model model, IFileLoader? fileLoader)
        {
            var table = fileLoader == null ? withoutLoader : withLoader;

            if (!table.TryGetValue(model, out var entry))
            {
                entry = new StrongBox<T>(create(model, fileLoader));
                table.AddOrUpdate(model, entry);
            }

            return entry.Value!;
        }
    }
}
