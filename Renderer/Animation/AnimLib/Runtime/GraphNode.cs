using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    public abstract partial class GraphNode
    {
        /// <summary>
        /// Wires child node references and definition-derived state. Runs once for every node in
        /// array order at graph construction (Esoterica InstantiateNode).
        /// </summary>
        public abstract void Instantiate(GraphContext ctx);

        private uint lastUpdateID = uint.MaxValue;

        /// <summary>True if this node has already been marked active during the current graph update.</summary>
        public bool WasUpdated(GraphContext ctx) => lastUpdateID == ctx.UpdateID;

        /// <summary>Marks this node as active/evaluated for the current graph update.</summary>
        public void MarkNodeActive(GraphContext ctx) => lastUpdateID = ctx.UpdateID;

        // Activation lifecycle (Esoterica GraphNode::Initialize/Shutdown): nodes are initialized
        // when their subtree becomes active and shut down when it deactivates, reference-counted
        // because value nodes can be shared by multiple active parents. Instances persist; the
        // lifecycle only resets internal state, so activation allocates nothing.
        private protected int initializationCount;

        /// <summary>Whether the node is active.</summary>
        public bool IsInitialized => initializationCount > 0;

        /// <summary>Activates the node, or adds a reference if it is already active.</summary>
        public virtual void Initialize(GraphContext ctx)
        {
            if (IsInitialized)
            {
                initializationCount++;
            }
            else
            {
                InitializeInternal(ctx);
            }
        }

        /// <summary>Releases a reference, deactivating the node when none remain.</summary>
        public void Shutdown(GraphContext ctx)
        {
            Debug.Assert(IsInitialized);
            if (initializationCount > 0 && --initializationCount == 0)
            {
                ShutdownInternal(ctx);
            }
        }

        /// <summary>Resets the node state when it becomes active.</summary>
        protected virtual void InitializeInternal(GraphContext ctx)
        {
            Debug.Assert(!IsInitialized);
            initializationCount++;
        }

        /// <summary>Clears the node state when it becomes inactive.</summary>
        protected virtual void ShutdownInternal(GraphContext ctx)
        {
            lastUpdateID = uint.MaxValue;
        }
    }

    public abstract partial class ValueNode
    {
        /// <inheritdoc/>
        public override void Instantiate(GraphContext ctx) { }
    }
}
