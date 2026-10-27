using System.Globalization;
using System.Linq;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>prop_animinteractable</c>: an animated model driven through its interaction sequence, such as a lever, crank or
/// roll-up door. A held use drags the interact bone along the path the sequence moves it; a tap plays it to the other end.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CAnimInteractable">CAnimInteractable</seealso>
public sealed class PropAnimInteractable : BaseModelEntity, IAimGrabbable
{
    private enum ReturnStyle
    {
        EaseInEaseOut = 0,
        EaseInOnly = 1,
        EaseOutOnly = 2,
        Bounce = 3,
        Lerp = 4,
    }

    private static readonly string[] TargetNames = ["A", "B", "C", "D", "E", "F"];

    // Interact bone positions over one cycle, in model space
    private const int PathSamples = 64;

    // A bone travelling less than this over a cycle turns in place, and the aim angle drives it instead
    private const float MinPathLength = 1f;

    // Degrees of aim pitch for one cycle of a bone that turns in place
    private const float PitchPerCycle = 90f;

    // How quickly a free resistance follows the aim, per second
    private const float FollowRate = 30f;

    private readonly List<AnimInteractablePart> parts = [];
    private int[] partBones = [];
    private Matrix4x4[] partBinds = [];

    private AnimationBase? sequence;
    private int interactBone = -1;
    private readonly Vector3[] path = new Vector3[PathSamples + 1];
    private bool followsPath;

    private float cycle;
    private float limitForward;
    private float limitBackward;
    private float limitLocked;
    private bool isLocked;
    private bool isInteractionDisabled;
    private bool onlyRunForward;
    private bool onlyRunBackward;
    private float animationDuration;
    private float positiveResistance;

    private bool returnsToCompletion;
    private float returnAmount;
    private float returnDelay;
    private ReturnStyle returnStyle;

    private readonly float[] targetValues = new float[6];
    private float targetThreshold;

    private string? startSound;
    private string? moveSound;
    private string? stopSound;
    private string? bounceSound;
    private string? lockedSound;
    private string? openCompleteSound;
    private string? closeCompleteSound;
    private SoundHandle moveSoundHandle;
    private bool isMoveSoundOn;

    private PlayerEntity? grabber;
    private float grabDistance;
    private float grabPitch;
    private Vector3 grabTarget;
    private float grabCompletion;

    // How far off the aim line the interact bone can be and still be taken hold of
    private const float AimRadius = 8f;

    // A move toward a goal, from a tap, an input or the return to completion
    private bool isMoving;
    private float moveFrom;
    private float moveGoal;
    private float moveTime;
    private float moveDuration;
    private ReturnStyle moveStyle;
    private float releasedTime = float.NegativeInfinity;

    /// <summary>Initializes a <c>prop_animinteractable</c> from its keyvalues.</summary>
    public PropAnimInteractable(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Gets the completion, in cycles of the interaction sequence from its start.</summary>
    public float Completion => cycle;

    // Collision does not follow the animated bones, and a solid open roll-up door would block the way
    /// <inheritdoc/>
    protected override bool BuildsCollider => false;

    /// <inheritdoc/>
    public override EntityCapability ObjectCaps => EntityCapability.ImpulseUse;

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        limitForward = KeyValues.GetFloatProperty("limitforward", 1f);
        limitBackward = KeyValues.GetFloatProperty("limitbackward");
        limitLocked = KeyValues.GetFloatProperty("limitlocked");
        isLocked = KeyValues.GetBooleanProperty("startlocked");
        isInteractionDisabled = KeyValues.GetBooleanProperty("startdisabled");
        onlyRunForward = KeyValues.GetBooleanProperty("onlyrunforward");
        onlyRunBackward = KeyValues.GetBooleanProperty("onlyrunbackward");
        animationDuration = MathF.Max(KeyValues.GetFloatProperty("animationduration", 1f), 0.05f);
        positiveResistance = KeyValues.GetFloatProperty("positiveresistance", 1f);

        returnsToCompletion = KeyValues.GetBooleanProperty("returntocompletion");
        returnAmount = KeyValues.GetFloatProperty("returntocompletionamount");
        returnDelay = KeyValues.GetFloatProperty("returntocompletiondelay");
        returnStyle = (ReturnStyle)KeyValues.GetInt32Property("returntocompletionstyle");

        for (var i = 0; i < targetValues.Length; i++)
        {
            targetValues[i] = KeyValues.GetFloatProperty("targetcompletionvalue" + TargetNames[i].ToLowerInvariant(), -1f);
        }

        targetThreshold = KeyValues.GetFloatProperty("targetcompletionthreshold");

        startSound = NonEmpty("startsound");
        moveSound = NonEmpty("movesound");
        stopSound = NonEmpty("stopsound");
        bounceSound = NonEmpty("bouncesound");
        lockedSound = NonEmpty("lockedsound");
        openCompleteSound = NonEmpty("opencompletesound");
        closeCompleteSound = NonEmpty("closecompletesound");

        cycle = Math.Clamp(KeyValues.GetFloatProperty("initialcompletionamount"), limitBackward, limitForward);

        PrepareSequence();
        ApplyPose();
        CreateParts();
    }

    // Each physics part follows the bone it is bound to, starting from where its bind pose puts it
    private void CreateParts()
    {
        if (ModelNode is not { } node || LoadedModel is not { } model || EntityCollider.LoadPhysics(model, EntitySystem.FileLoader) is not { } physics)
        {
            return;
        }

        // Collision cannot scale, so a scaled model stays passable rather than blocking at the wrong size
        if (Vector3.Distance(EntityScale, Vector3.One) > 1e-3f)
        {
            return;
        }

        var controller = node.AnimationController;
        var physicsBinds = physics.BindPose;
        var boneNames = physics.BoneNames;

        partBones = new int[physics.Parts.Length];
        partBinds = new Matrix4x4[physics.Parts.Length];

        for (var i = 0; i < physics.Parts.Length; i++)
        {
            var bone = i < boneNames.Length ? controller.Skeleton.GetBoneIndex(boneNames[i]) : -1;
            var partBind = i < physicsBinds.Length ? physicsBinds[i] : Matrix4x4.Identity;

            partBones[i] = bone;
            partBinds[i] = bone >= 0 ? partBind * controller.InverseBindPose[bone] : partBind;

            var part = new AnimInteractablePart(this, physics, i, IsRigidBodySimulated);
            parts.Add(part);
            EntitySystem.AddEntity(part);
        }

        PlaceParts();
    }

    private Matrix4x4 PartPose(int index)
    {
        var controller = ModelNode!.AnimationController;
        var bone = partBones[index];
        var model = bone >= 0 ? partBinds[index] * controller.Pose[bone] : partBinds[index];

        return model * ModelToWorld;
    }

    private void PlaceParts()
    {
        for (var i = 0; i < parts.Count; i++)
        {
            parts[i].Place(PartPose(i));
        }
    }

    private string? NonEmpty(string key) => KeyValues.GetStringProperty(key) is { Length: > 0 } value ? value : null;

    // The sequence the interact bone travels furthest in, sampled once into a path
    private void PrepareSequence()
    {
        if (ModelNode is not { } node)
        {
            return;
        }

        var controller = node.AnimationController;
        var boneName = KeyValues.GetStringProperty("interactionbonename") is { Length: > 0 } name ? name : "interact";
        interactBone = controller.Skeleton.GetBoneIndex(boneName);

        // Two handed and multi part models number their interact bones, or prefix them
        if (interactBone < 0 && controller.Skeleton.Bones.FirstOrDefault(bone => bone.Name.Contains(boneName, StringComparison.OrdinalIgnoreCase)) is { } match)
        {
            interactBone = match.Index;
        }

        // The first sequence that moves the interact bone, as variants for each hand may follow it
        foreach (var animation in node.Animations.Values)
        {
            sequence ??= animation;

            if (SamplePath(node, animation) >= MinPathLength)
            {
                sequence = animation;
                break;
            }
        }

        if (sequence != null)
        {
            followsPath = SamplePath(node, sequence) >= MinPathLength;
        }
    }

    private float SamplePath(SceneNodes.ModelSceneNode node, AnimationBase animation)
    {
        var controller = node.AnimationController;
        node.SetAnimation(animation);
        controller.IsPaused = true;

        if (interactBone < 0)
        {
            return 0f;
        }

        var length = 0f;

        for (var i = 0; i <= PathSamples; i++)
        {
            controller.Time = animation.Duration * i / PathSamples;
            controller.Update(0f);
            path[i] = controller.Pose[interactBone].Translation;

            if (i > 0)
            {
                length += Vector3.Distance(path[i - 1], path[i]);
            }
        }

        return length;
    }

    // A completion past one plays the sequence again, as a crank turned several times
    private static float SequencePosition(float completion)
    {
        var position = completion - MathF.Floor(completion);
        return position == 0f && completion > 0f ? 1f : position;
    }

    private void ApplyPose()
    {
        if (ModelNode is not { } node || sequence == null)
        {
            return;
        }

        var controller = node.AnimationController;

        if (controller.ActiveAnimation != sequence)
        {
            node.SetAnimation(sequence);
        }

        controller.IsPaused = true;
        controller.Time = sequence.Duration * SequencePosition(cycle);
        controller.Update(0f);
    }

    private Vector3 PathPoint(float position)
    {
        var scaled = Math.Clamp(position, 0f, 1f) * PathSamples;
        var index = Math.Min((int)scaled, PathSamples - 1);
        return Vector3.Lerp(path[index], path[index + 1], scaled - index);
    }

    private Matrix4x4 ModelToWorld => ModelNode?.Transform ?? Matrix4x4.Identity;

    /// <inheritdoc/>
    public bool TryAim(Vector3 eyePosition, Vector3 forward, float reach, out float distance)
    {
        distance = reach;

        if (isInteractionDisabled || ModelNode == null)
        {
            return false;
        }

        var found = false;

        // Any of its parts, which is where the aim lands on a wide model
        foreach (var part in parts)
        {
            if (part.Collider is not { IsEmpty: false } collider)
            {
                continue;
            }

            var hit = collider.TraceRay(eyePosition, eyePosition + forward * reach, "player");

            if (hit.Hit && hit.Distance < distance)
            {
                distance = hit.Distance;
                found = true;
            }
        }

        if (interactBone >= 0)
        {
            var point = Vector3.Transform(PathPoint(SequencePosition(cycle)), ModelToWorld);
            var along = Vector3.Dot(point - eyePosition, forward);

            if (along > 0f && along < distance && Vector3.Distance(point, eyePosition + forward * along) <= AimRadius)
            {
                distance = along;
                found = true;
            }
        }

        return found;
    }

    /// <inheritdoc/>
    public void BeginAimGrab(PlayerEntity player, Vector3 eyePosition, Vector3 forward, float distance)
    {
        grabber = player;
        grabDistance = distance;
        grabTarget = AimedPoint(eyePosition, forward);
        grabPitch = PitchOf(forward);
        grabCompletion = cycle;
        isMoving = false;

        if (isLocked)
        {
            PlaySound(lockedSound);
        }
        else
        {
            PlaySound(startSound);
        }

        EntitySystem.TriggerOutput(this, "OnInteractStart", player);
    }

    /// <inheritdoc/>
    public void UpdateAimGrab(Vector3 eyePosition, Vector3 forward, float deltaTime)
    {
        if (grabber == null || sequence == null || deltaTime <= 0f)
        {
            return;
        }

        // The aim moves the completion by how far it travels along the bone path, wherever on the model it
        // took hold, rather than pulling the bone onto the aimed point
        if (followsPath)
        {
            var target = AimedPoint(eyePosition, forward);
            var position = SequencePosition(cycle);
            const float Step = 1f / PathSamples;
            var tangent = (PathPoint(position + Step) - PathPoint(position - Step)) / (2f * Step);
            var slope = tangent.LengthSquared();

            if (slope > 1e-6f)
            {
                grabCompletion += Vector3.Dot(target - grabTarget, tangent) / slope;
            }

            grabTarget = target;
        }
        else
        {
            var pitch = PitchOf(forward);
            grabCompletion += (grabPitch - pitch) / PitchPerCycle;
            grabPitch = pitch;
        }

        grabCompletion = Math.Clamp(grabCompletion, limitBackward, isLocked ? MathF.Min(limitForward, limitLocked) : limitForward);
        var delta = grabCompletion - cycle;

        // A resistance below one lags the aim, as a heavy door does
        if (positiveResistance < 1f)
        {
            delta *= MathF.Min(1f, MathF.Max(positiveResistance, 0f) * FollowRate * deltaTime);
        }

        SetCycle(cycle + Math.Clamp(delta, -0.25f, 0.25f));
    }

    // In model space, where the bone path is
    private Vector3 AimedPoint(Vector3 eyePosition, Vector3 forward)
    {
        Matrix4x4.Invert(ModelToWorld, out var worldToModel);
        return Vector3.Transform(eyePosition + forward * grabDistance, worldToModel);
    }

    /// <inheritdoc/>
    public void EndAimGrab()
    {
        if (grabber == null)
        {
            return;
        }

        var player = grabber;
        grabber = null;
        releasedTime = EntitySystem.CurrentTime;

        StopMoveSound();
        PlaySound(stopSound);
        EntitySystem.TriggerOutput(this, "OnInteractStop", player);
    }

    private static float PitchOf(Vector3 forward) => float.RadiansToDegrees(MathF.Asin(Math.Clamp(-forward.Z, -1f, 1f)));

    /// <summary>Plays the sequence to its far end from the near one, as a tap of use.</summary>
    public override void Use(BaseEntity? activator)
    {
        if (isInteractionDisabled || grabber != null)
        {
            return;
        }

        if (isLocked)
        {
            PlaySound(lockedSound);
            return;
        }

        var goal = cycle - limitBackward < (limitForward - limitBackward) * 0.5f ? limitForward : limitBackward;
        MoveTo(goal, ReturnStyle.EaseInEaseOut);
        EntitySystem.TriggerOutput(this, "OnInteractStart", activator);
    }

    private void MoveTo(float goal, ReturnStyle style)
    {
        goal = Math.Clamp(goal, limitBackward, isLocked ? MathF.Min(limitForward, limitLocked) : limitForward);

        if (MathF.Abs(goal - cycle) < 1e-4f)
        {
            return;
        }

        isMoving = true;
        moveFrom = cycle;
        moveGoal = goal;
        moveTime = 0f;
        moveDuration = animationDuration * MathF.Abs(goal - cycle);
        moveStyle = style;
    }

    private static float Ease(ReturnStyle style, float t) => style switch
    {
        ReturnStyle.EaseInOnly or ReturnStyle.Bounce => t * t,
        ReturnStyle.EaseOutOnly => 1f - (1f - t) * (1f - t),
        ReturnStyle.Lerp => t,
        _ => t * t * (3f - 2f * t),
    };

    /// <inheritdoc/>
    internal override void FrameSimulate(float frameTime)
    {
        base.FrameSimulate(frameTime);
        UpdateMotion(frameTime);

        for (var i = 0; i < parts.Count; i++)
        {
            parts[i].Steer(PartPose(i), frameTime);
        }
    }

    private void UpdateMotion(float frameTime)
    {
        if (grabber != null)
        {
            return;
        }

        if (!isMoving && returnsToCompletion && EntitySystem.CurrentTime - releasedTime >= returnDelay)
        {
            var goal = returnAmount < 0f ? KeyValues.GetFloatProperty("initialcompletionamount") : returnAmount;

            if (MathF.Abs(goal - cycle) > 1e-4f)
            {
                MoveTo(goal, returnStyle);

                if (isMoving)
                {
                    EntitySystem.TriggerOutput(this, "OnReturnToCompletion", this);
                }
            }
        }

        if (!isMoving)
        {
            return;
        }

        moveTime += frameTime;
        var t = moveDuration > 0f ? MathF.Min(moveTime / moveDuration, 1f) : 1f;
        SetCycle(float.Lerp(moveFrom, moveGoal, Ease(moveStyle, t)));

        if (t >= 1f)
        {
            isMoving = false;
            StopMoveSound();

            if (moveStyle == ReturnStyle.Bounce)
            {
                PlaySound(bounceSound);
            }
        }
    }

    private void SetCycle(float value)
    {
        var low = limitBackward;
        var high = isLocked ? MathF.Min(limitForward, limitLocked) : limitForward;
        var next = Math.Clamp(value, low, high);

        if (grabber != null && ((onlyRunForward && next < cycle) || (onlyRunBackward && next > cycle)))
        {
            return;
        }

        if (MathF.Abs(next - cycle) < 1e-5f)
        {
            return;
        }

        var previous = cycle;
        cycle = next;
        ApplyPose();
        StartMoveSound();

        if (previous < high && cycle >= high && !isLocked)
        {
            PlaySound(openCompleteSound);
        }
        else if (previous > low && cycle <= low)
        {
            PlaySound(closeCompleteSound);
        }

        FireCompletionOutputs(previous, cycle);
    }

    private void FireCompletionOutputs(float previous, float current)
    {
        var activator = (BaseEntity?)grabber ?? this;

        for (var i = 0; i < targetValues.Length; i++)
        {
            var value = targetValues[i];

            if (value < 0f)
            {
                continue;
            }

            var wasInside = MathF.Abs(previous - value) <= targetThreshold;
            var isInside = MathF.Abs(current - value) <= targetThreshold;

            if (!wasInside && isInside)
            {
                EntitySystem.TriggerOutput(this, "OnCompletion" + TargetNames[i], activator);
                EntitySystem.TriggerOutput(this, "OnCompletion" + TargetNames[i] + (current > previous ? "_Forward" : "_Backward"), activator);
            }
            else if (wasInside && !isInside)
            {
                EntitySystem.TriggerOutput(this, "OnCompletionExit" + TargetNames[i], activator);
            }
        }

        var range = limitForward - limitBackward;
        var normalized = range > 0f ? (current - limitBackward) / range : 0f;

        EntitySystem.TriggerOutput(this, "Position", activator, normalized.ToString(CultureInfo.InvariantCulture));
        EntitySystem.TriggerOutput(this, "PositionInverted", activator, (1f - normalized).ToString(CultureInfo.InvariantCulture));
        EntitySystem.TriggerOutput(this, "PositionRaw", activator, current.ToString(CultureInfo.InvariantCulture));
    }

    private void StartMoveSound()
    {
        if (isMoveSoundOn || moveSound == null)
        {
            return;
        }

        moveSoundHandle = Sound.Play(moveSound, WorldOrigin);
        isMoveSoundOn = true;
    }

    private void StopMoveSound()
    {
        if (!isMoveSoundOn)
        {
            return;
        }

        moveSoundHandle.Stop();
        moveSoundHandle = default;
        isMoveSoundOn = false;
    }

    private void PlaySound(string? soundEvent)
    {
        if (soundEvent != null)
        {
            Sound.Play(soundEvent, WorldOrigin);
        }
    }

    [EntityInput("Lock")]
    private void InputLock(EntityInputData data) => isLocked = true;

    [EntityInput("Unlock")]
    private void InputUnlock(EntityInputData data) => isLocked = false;

    [EntityInput("SetCompletionValue")]
    private void InputSetCompletionValue(EntityInputData data) => MoveTo(data.Float(), ReturnStyle.EaseInEaseOut);

    [EntityInput("EnableReturnToCompletion")]
    private void InputEnableReturnToCompletion(EntityInputData data) => returnsToCompletion = true;

    [EntityInput("DisableReturnToCompletion")]
    private void InputDisableReturnToCompletion(EntityInputData data) => returnsToCompletion = false;

    [EntityInput("SetReturnToCompletionAmount")]
    private void InputSetReturnToCompletionAmount(EntityInputData data) => returnAmount = data.Float();

    [EntityInput("ForceImmediateReturnToCompletion")]
    private void InputForceImmediateReturnToCompletion(EntityInputData data) => SetCycle(returnAmount < 0f ? KeyValues.GetFloatProperty("initialcompletionamount") : returnAmount);

    [EntityInput("SetLimitForward")]
    private void InputSetLimitForward(EntityInputData data) => limitForward = data.Float();

    [EntityInput("SetLimitBackward")]
    private void InputSetLimitBackward(EntityInputData data) => limitBackward = data.Float();

    [EntityInput("SetAnimationDuration")]
    private void InputSetAnimationDuration(EntityInputData data) => animationDuration = MathF.Max(data.Float(), 0.05f);

    [EntityInput("SetPositiveResistance")]
    private void InputSetPositiveResistance(EntityInputData data) => positiveResistance = data.Float();

    [EntityInput("DisableInteraction")]
    private void InputDisableInteraction(EntityInputData data)
    {
        isInteractionDisabled = true;
        grabber?.ReleaseAimGrab();
    }

    [EntityInput("EnableInteraction")]
    private void InputEnableInteraction(EntityInputData data) => isInteractionDisabled = false;

    [EntityInput("ForcePlayerRelease")]
    private void InputForcePlayerRelease(EntityInputData data) => grabber?.ReleaseAimGrab();

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        grabber?.ReleaseAimGrab();
        StopMoveSound();

        foreach (var part in parts)
        {
            EntitySystem.Remove(part);
        }

        parts.Clear();

        base.OnRemove();
    }
}
