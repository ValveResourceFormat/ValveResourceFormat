using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class IDValueNode
    {
        GlobalSymbol cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public GlobalSymbol GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            ctx.LogNodeNotImplemented(NodeIdx, GetType().Name);
            return default;
        }
    }

    partial class CachedIDNode
    {
        IDValueNode InputValueNode;
        GlobalSymbol CachedValue;
        bool HasCachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            if (!HasCachedValue)
            {
                Debug.Assert(Mode == CachedValueMode.OnExit);

                if (ctx.BranchState == BranchState.Inactive)
                {
                    HasCachedValue = true;
                }
                else
                {
                    CachedValue = InputValueNode.GetValue(ctx);
                }
            }

            return CachedValue;
        }
    }

    partial class ConstIDNode
    {
        protected override GlobalSymbol GetValueInternal(GraphContext ctx) => Value;
    }

    partial class ControlParameterIDNode
    {
        string parameterName;

        public override void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Graph.ParameterNames.Length);
            parameterName = ctx.Graph.ParameterNames[NodeIdx];
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            return new GlobalSymbol(ctx.Graph.IdParameters[parameterName]);
        }
    }

    // Returns the ID of the sync event the source state is currently in.
    partial class CurrentSyncEventIDNode
    {
        StateNode SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            var syncTrack = SourceStateNode.SyncTrack;
            var currentSyncTime = syncTrack.GetTime(SourceStateNode.CurrentTime);
            return syncTrack.GetEventID(currentSyncTime.EventIdx);
        }
    }

    // A virtual parameter is a graph-computed sub-expression: evaluates its child (cached once per update).
    partial class VirtualParameterIDNode
    {
        IDValueNode ChildNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the IDValueNode base.
        protected override GlobalSymbol GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
