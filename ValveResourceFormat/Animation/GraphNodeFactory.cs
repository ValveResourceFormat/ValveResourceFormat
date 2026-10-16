using System.Collections.Concurrent;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Animation
{
    /// <summary>
    /// Creates an animation graph node from its compiled definition.
    /// </summary>
    /// <param name="definition">The <c>m_nodes</c> entry of the graph definition.</param>
    public delegate GraphNode GraphNodeCreator(KVObject definition);

    /// <summary>
    /// Maps compiled graph node classes to the runtime nodes that evaluate them.
    /// </summary>
    /// <remarks>
    /// The nodes of this library and of the renderer are registered by a source generator when their
    /// assembly loads. Other code adds its own nodes through <see cref="Register{TNode}"/>, or replaces
    /// built-in ones through <see cref="Register(string, GraphNodeCreator)"/>.
    /// </remarks>
    public static class GraphNodeFactory
    {
        // Graphs load on several threads, and registrations may come in later
        private static readonly ConcurrentDictionary<string, GraphNodeCreator> Creators = new(StringComparer.Ordinal);

        /// <summary>
        /// Registers a node under the compiled class named after it, so that <c>ClipNode</c> is created for
        /// <c>CNmClipNode::CDefinition</c>, replacing any earlier registration.
        /// </summary>
        /// <typeparam name="TNode">The node type, named as its compiled class is without the <c>CNm</c> prefix.</typeparam>
        /// <param name="creator">Creates the node from its definition.</param>
        public static void Register<TNode>(Func<KVObject, TNode> creator) where TNode : GraphNode
        {
            Register($"CNm{typeof(TNode).Name}::CDefinition", definition => creator(definition));
        }

        /// <summary>
        /// Registers the node created for a compiled class, replacing any earlier registration.
        /// </summary>
        /// <param name="className">The <c>_class</c> of the compiled definition, such as <c>CNmClipNode::CDefinition</c>.</param>
        /// <param name="creator">Creates the node from its definition.</param>
        public static void Register(string className, GraphNodeCreator creator)
        {
            Creators[className] = creator;
        }

        /// <summary>Gets whether a node is registered for a compiled class.</summary>
        public static bool IsSupported(string className) => Creators.ContainsKey(className);

        /// <summary>Creates the node for one <c>m_nodes</c> entry of a graph definition.</summary>
        /// <exception cref="InvalidOperationException">No node is registered for the definition's class.</exception>
        public static GraphNode Create(KVObject definition)
        {
            var className = definition.GetStringProperty("_class")
                ?? throw new InvalidOperationException("Graph node has no _class property.");

            if (!Creators.TryGetValue(className, out var creator))
            {
                throw new InvalidOperationException($"Unknown graph node type {className}.");
            }

            return creator(definition);
        }
    }
}
