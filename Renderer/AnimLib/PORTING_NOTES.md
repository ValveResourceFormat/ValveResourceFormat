# Esoterica port notes

The C++ in Esoterica (`Code/Engine/Animation/Graph`) is the reference; node semantics are ported
verbatim wherever possible. Deliberate architecture deviations:

- No task system: blends run directly on parent-space `FrameBone[]` pose buffers. Cached poses
  (forced transitions) are pooled `Transform[]` buffers on the graph context instead of task-system
  buffers, and bone mask task lists evaluate into pooled per-bone weight arrays instead of
  registering tasks.
- No root motion debugger.
- `Transform` is a project alias of `FrameBone` (uniform scale).

The node activation lifecycle is ported faithfully: `Instantiate` wires references once at graph
construction (Esoterica InstantiateNode), and the refcounted `Initialize`/`Shutdown` pair runs on
persistent instances as subtrees activate/deactivate — nothing is created or destroyed at runtime,
and activation allocates nothing. Persistent nodes (`m_persistentNodeIndices` = control + virtual
parameters) initialize at instance creation; the root initializes lazily on the first update with
an update-ID bump so init-time value reads recompute (GraphInstance::ResetGraphState). The
state-machine transition-condition swap uses the immediate shutdown form (Esoterica HEAD defers
the old state's condition shutdown by one update for debug visualization only).

## Known remaining divergences (from the 2026-07 fidelity audit)

Behavioral, not yet ported:

- Clip root motion sampling (`RootMotionDelta` stays identity); `VelocityBlendNode` and
  `VelocityBasedSpeedScaleNode` need per-clip average velocity from decoded root motion. This also
  covers the reversed-playback root-motion special case (play-in-reverse itself is ported; no CS2
  graph authors it — Tests/AnimGraphReverseTest.cs drives it synthetically).
- `SnapToFrameEvent` pose-time snapping.
- Transition start bone mask (`m_startBoneMaskNodeIdx` / `m_boneMaskBlendInTimePercentage`
  pose-weight remap during the blend-in).
- `Blend2DNode` single-source case self-blends the sync track (re-basing event start times)
  instead of copying it with `ClearStartOffset`.
- Sampled event tracking (`m_newEvents` / continuous / ended buffers) is not ported; the buffer is
  cleared each update.
- `LayerContext.IsAdditive` is not tracked (C++ children use it to pick zero vs reference pose
  defaults and the root-motion blend mode).
- `FloatCurveEventNode`: the event search is ported, but curve evaluation is not
  (`CNmFloatCurveEvent` is not surfaced as a typed clip event); a matched event keeps the default
  value and logs once. Exactly one CS2 clip contains such an event and no CS2 graph uses the node.
- Foot events (`FootEventConditionNode`, `FootstepEventIDNode`,
  `FootstepEventPercentageThroughNode`) and `TransitionEventConditionNode` are ported reading the
  raw KV of `CNmFootEvent`/`CNmTransitionEvent` clip events; no CS2 clip contains either event
  class (scanned 2355 clips), so in practice these nodes return their no-event defaults, matching
  C++ behavior with no events found.

Cosmetic/numeric: C++ uses `FastSLerp` for blend rotations (we use exact `Slerp`); easing `Expo`
has a `-0.001` offset upstream; `Range.GetClampedValue` throws on authored inverted ranges;
`TargetInfoNode` axis conventions (forward/right) need verification against a known-good graph;
`Blender.LerpMatrix` decomposes matrices where C++ blends transforms directly.

CS2 additions with no Esoterica analogue: `IsInactiveBranchConditionNode` (implemented as
"currently evaluating an inactive branch").

Viewer-only additions with no C++ analogue: `AnimationGraph.ForceLoopingClips` (UI toggle),
reference-pose-initialized node buffers (unwritten buffers yield bind pose instead of garbage).

## CS2 data notes (vpk scan, 2026-07)

Across all 231 graphs the only node classes that appear are the ~55 listed by the scan harness;
notably `GraphEventConditionNode` (183 instances) and `IDSwitchNode` (37) were the only
previously-unimplemented ones in real use. Clip events across 2355 clips: Sound (2002), ID (1147),
Particle (189), OrientationWarp (26), MaterialAttribute (15), Legacy (3), FloatCurve (1) — no foot
or transition events. Forced transitions are common: 699 of 10863 transition definitions across
159 graphs (knife viewmodels and worldmodel locomotion especially).
