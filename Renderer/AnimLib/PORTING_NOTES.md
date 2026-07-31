# Esoterica port notes

The C++ in Esoterica (`Code/Engine/Animation/Graph`) is the reference; node semantics are ported
verbatim wherever possible. Deliberate architecture deviations:

- No task system: blends run directly on parent-space `FrameBone[]` pose buffers.
- No runtime node create/destroy: `PoseNode.Restart(ctx)` stands in for the initialize/shutdown
  lifecycle; states restart their subtree on entry.
- No cached pose buffers, no root motion debugger.
- `Transform` is a project alias of `FrameBone` (uniform scale).

## Known remaining divergences (from the 2026-07 fidelity audit)

Behavioral, not yet ported:

- Forced transitions: no `NotifyNewTransitionStarting` — an in-flight transition whose source is
  about to become the new target is not converted to a cached pose, so the same state can be
  updated twice in one frame. Needs cached pose support.
- Clip play-in-reverse (`m_playInReverseValueNode`): value read, not applied.
- Clip root motion sampling (`RootMotionDelta` stays identity); `VelocityBlendNode` and
  `VelocityBasedSpeedScaleNode` need per-clip average velocity from decoded root motion.
- `SnapToFrameEvent` pose-time snapping.
- Transition start bone mask (`m_startBoneMaskNodeIdx` / `m_boneMaskBlendInTimePercentage`
  pose-weight remap).
- `BoneMaskTaskList` is minimal (single mask or uniform weight); mask blend tasks
  (`SetToBlendBetweenTaskLists`) pick the dominant list instead of blending, `BoneMaskBlendNode`'s
  intermediate branch returns the source mask, `BoneMaskSwitchNode` ignores `SwitchDynamically`
  and its blend time. The C++ default mask list has tasks (weight 1); ours reports unset, which
  downgrades an unmasked ModelSpace layer to Overlay with a warning.
- `Cached*Node` `CachedValueMode.OnEntry` behaves as `OnExit`; cached values and
  `FloatEaseNode`/`FloatSelectorNode` easing state survive `Restart` (no value-node lifecycle).
- Layer context transition blending: `TransitionNode.Update` does not lerp source/target layer
  contexts; both sides multiply into the same context.
- `Blend2DNode` single-source case self-blends the sync track (re-basing event start times)
  instead of copying it with `ClearStartOffset`.
- Sampled event tracking (`m_newEvents` / continuous / ended buffers) is not ported; the buffer is
  cleared each update.
- `LayerContext.IsAdditive` is not tracked (C++ children use it to pick zero vs reference pose
  defaults and the root-motion blend mode).

Unimplemented value nodes (log once, return defaults): `VectorInfoNode` (also mistyped input),
`VectorCreateNode`, `VectorNegateNode`, `IDSwitchNode`, `IDSelectorNode`, `IDEventNode`,
`IDEventPercentageThroughNode`, `GraphEventConditionNode`, `FootEventConditionNode`,
`FootstepEventIDNode`, `FootstepEventPercentageThroughNode`, `FloatCurveEventNode`,
`TransitionEventConditionNode`, `IsExternalPoseSetNode`, `IsExternalGraphSlotFilledNode`,
`IsInactiveBranchConditionNode`.

Cosmetic/numeric: C++ uses `FastSLerp` for blend rotations (we use exact `Slerp`); easing `Expo`
has a `-0.001` offset upstream; `Range.GetClampedValue` throws on authored inverted ranges;
`TargetInfoNode` axis conventions (forward/right) need verification against a known-good graph;
`Blender.LerpMatrix` decomposes matrices where C++ blends transforms directly.

Viewer-only additions with no C++ analogue: `AnimationGraph.ForceLoopingClips` (UI toggle),
reference-pose-initialized node buffers (unwritten buffers yield bind pose instead of garbage).
