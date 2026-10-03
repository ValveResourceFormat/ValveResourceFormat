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

## Remaining divergences

- Sampled event tracking (new / continuous / ended event lists) is not ported; the buffer is
  cleared each update. Only game code and debug views consume it, and the viewer consumes neither.
- `OrientationWarpNode.m_bWarpTranslation` (Valve addition) is not applied. `AnimationEndFacing`
  alignment aligns the clip's end facing instead of its post-warp movement direction.
- `SnapWeaponNode` (client.dll) snaps the weapon and solves the left hand, but its molotov lighter
  and knife push modes drive a secondary weapon skeleton pose the graph does not evaluate.
- The world transform the graph sees is its own accumulated root motion, starting at the origin.
- `TargetWarpNode` and `RootMotionOverrideNode` have no CS2 users, so they are only exercised by
  construction and the regression sweep.

Axis conventions: Esoterica's `Vector::WorldForward` is -Y, but the compiled graph and clip data use
Source axes, so `TransformMath` defines forward +X, left +Y, up +Z (chicken run root motion moves along
+X). Esoterica `a * b` on quaternions is Hamilton `b * a` in System.Numerics; transform products keep
their order. Valve's own helpers compose `A o B` as B expressed in A's space, which is `B * A`.

CS2 additions with no Esoterica analogue, reverse engineered from animationsystem.dll and client.dll:
`ChainLookatNode`, `FollowBoneNode` (its partial modes treat the bone's local value as model space,
ported as is), `BodyGroupNode` (emits its event every update while enabled), `SnapWeaponNode`,
`IsInactiveBranchConditionNode` (implemented as "currently evaluating an inactive branch").

Viewer-only additions with no C++ analogue: `AnimationGraph.ForceLoopingClips` (UI toggle),
reference-pose-initialized node buffers (unwritten buffers yield bind pose instead of garbage).

## CS2 data notes (vpk scan, 2026-07)

Across all 231 graphs the only node classes that appear are the ~55 listed by the scan harness;
notably `GraphEventConditionNode` (183 instances) and `IDSwitchNode` (37) were the only
previously-unimplemented ones in real use. Clip events across 2355 clips: Sound (2002), ID (1147),
Particle (189), OrientationWarp (26), MaterialAttribute (15), Legacy (3), FloatCurve (1) — no foot
or transition events. Forced transitions are common: 699 of 10863 transition definitions across
159 graphs (knife viewmodels and worldmodel locomotion especially).
