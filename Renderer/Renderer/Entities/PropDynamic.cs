using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>A dynamic model.</summary>
public sealed class PropDynamic : BaseModelEntity, IDamageable
{
    /// <summary>Spawn flags for dynamic props.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Spawns with collision off, until <c>EnableCollision</c>.</summary>
        StartCollisionDisabled = 256,
    }

    /// <summary>Gets the animation the prop returns to once another one ends, if it has one.</summary>
    public string? IdleAnimation { get; private set; }

    /// <summary>Gets whether an ended animation stays on its last frame instead of returning to <see cref="IdleAnimation"/>.</summary>
    public bool HoldAnimation { get; private set; }

    /// <summary>Gets the damage left before the prop breaks.</summary>
    public float Health { get; private set; }

    /// <summary>Gets whether the prop can still be broken: its model has health, and it has not broken yet.</summary>
    public bool IsBreakable => breakData?.IsBreakable == true && !isBroken;

    // A window pane, a shop front: a dynamic prop breaks like a physics one, into physics pieces
    private PropBreakData? breakData;
    private bool isBroken;

    // Null leaves looping to the animation itself, see PlayAnimation
    private bool? idleLooping;
    private bool hasCollision;
    private bool isForcedAnimation;
    private bool isWaitingForAnimationEnd;

    /// <summary>Initializes a <c>prop_dynamic</c> from its keyvalues.</summary>
    public PropDynamic(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        // CS2 names the starting and idle animations apart; Source 1 maps have one default for both
        IdleAnimation = NonEmpty(KeyValues.GetStringProperty("idleanim")) ?? NonEmpty(KeyValues.GetStringProperty("defaultanim"));
        idleLooping = ReadLoopMode("idleanimationloopmode");
        HoldAnimation = KeyValues.GetBooleanProperty("holdanimation");

        // The collider is rigid and ignores scale, and blocking with a hull of the wrong size is worse
        // than not blocking at all
        hasCollision = Solid != SolidType.SOLID_NONE && EntityScale == Vector3.One;
        IsSolid = hasCollision && !HasSpawnFlags(SpawnFlag.StartCollisionDisabled);
        IsDrawn = !KeyValues.GetBooleanProperty("startdisabled");

        if (LoadedModel is { } model)
        {
            breakData = EntitySystem.PropData.Resolve(model);
            Health = breakData.Health;
        }

        // HL:A only
        if (KeyValues.GetInt32Property("setbodygroup") is > 0 and var bodyGroupChoice)
        {
            SetBodyGroup(bodyGroupChoice.ToString(CultureInfo.InvariantCulture));
        }

        // Replayed over what BaseModelEntity posed, which neither knows the loop mode nor plays a held
        // animation through before stopping on its last frame
        if (NonEmpty(KeyValues.GetStringProperty("startinganim")) is { } startingAnimation)
        {
            PlayAnimation(startingAnimation, ReadLoopMode("startinganimationloopmode"), restart: true, forced: false);
        }
        else if (IdleAnimation != null)
        {
            PlayAnimation(IdleAnimation, idleLooping, restart: true, forced: false);
        }
    }

    /// <inheritdoc/>
    public override void Think()
    {
        if (!isWaitingForAnimationEnd || ModelNode is not { } node)
        {
            return;
        }

        // The node advances its animation per frame, so the end is noticed on the first tick after it
        if (!HasAnimationEnded(node.AnimationController))
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
            return;
        }

        isWaitingForAnimationEnd = false;

        if (isForcedAnimation)
        {
            isForcedAnimation = false;

            EntitySystem.TriggerOutput(this, "OnAnimationReachedEnd");
            EntitySystem.TriggerOutput(this, "OnAnimationDone");
        }

        ReturnToIdle();
    }

    /// <inheritdoc/>
    public void TakeDamage(in DamageInfo info)
    {
        if (!IsBreakable)
        {
            return;
        }

        var amount = info.Amount * breakData!.ScaleFor(info.Type);

        if (amount <= 0f)
        {
            return;
        }

        Health -= amount;
        EntitySystem.TriggerOutput(this, "OnHealthChanged", info.Attacker);

        if (Health <= 0f)
        {
            Break(info.Attacker, info.Direction);
        }
    }

    /// <summary>
    /// Breaks the prop: <c>OnBreak</c> fires, the pieces its model lists spawn where their parts of it
    /// were, and the prop is removed.
    /// </summary>
    /// <param name="attacker">Who broke it, for <c>OnBreak</c>.</param>
    /// <param name="direction">Which way the breaking hit travelled, for the effects; zero when unknown.</param>
    public void Break(BaseEntity? attacker = null, Vector3 direction = default)
    {
        if (isBroken || breakData == null)
        {
            return;
        }

        isBroken = true;
        Health = 0f;

        EntitySystem.TriggerOutput(this, "OnBreak", attacker);

        var rotation = EntityTransformHelper.EulerAnglesToQuaternion(Angles);
        PropPhysics.BreakApart(this, breakData, Origin, rotation, Vector3.Zero, Vector3.Zero, RootNode?.BoundingBox.Center ?? Origin, direction);

        EntitySystem.Remove(this);
    }

    [EntityInput("Break")]
    private void InputBreak(EntityInputData data) => Break(data.Activator);

    [EntityInput("SetAnimationLooping")]
    private void InputSetAnimationLooping(EntityInputData data) => PlayForcedAnimation(data, looping: true, restart: true);

    [EntityInput("SetAnimationNotLooping")]
    private void InputSetAnimationNotLooping(EntityInputData data) => PlayForcedAnimation(data, looping: false, restart: true);

    [EntityInput("SetAnimationNoResetLooping")]
    private void InputSetAnimationNoResetLooping(EntityInputData data) => PlayForcedAnimation(data, looping: true, restart: false);

    [EntityInput("SetAnimationNoResetNotLooping")]
    private void InputSetAnimationNoResetNotLooping(EntityInputData data) => PlayForcedAnimation(data, looping: false, restart: false);

    [EntityInput("SetAnimation")]
    private void InputSetAnimation(EntityInputData data) => PlayForcedAnimation(data, looping: null, restart: true);

    [EntityInput("SetAnimationNoReset")]
    private void InputSetAnimationNoReset(EntityInputData data) => PlayForcedAnimation(data, looping: null, restart: false);

    [EntityInput("SetIdleAnimationLooping")]
    private void InputSetIdleAnimationLooping(EntityInputData data) => SetIdleAnimation(data, looping: true);

    [EntityInput("SetIdleAnimationNotLooping")]
    private void InputSetIdleAnimationNotLooping(EntityInputData data) => SetIdleAnimation(data, looping: false);

    [EntityInput("SetDefaultAnimation")]
    private void InputSetDefaultAnimation(EntityInputData data) => SetIdleAnimation(data, looping: null);

    [EntityInput("SetPlaybackRate")]
    private void InputSetPlaybackRate(EntityInputData data)
    {
        // Playing backwards is not simulated, so a negative rate holds the pose instead
        ModelNode?.AnimationController.FrametimeMultiplier = MathF.Max(data.Float(1f), 0f);
    }

    [EntityInput("TurnOn")] private void InputTurnOn(EntityInputData data) => IsDrawn = true;

    [EntityInput("TurnOff")] private void InputTurnOff(EntityInputData data) => IsDrawn = false;

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => IsDrawn = true;

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => IsDrawn = false;

    [EntityInput("EnableCollision")] private void InputEnableCollision(EntityInputData data) => IsSolid = hasCollision;

    [EntityInput("DisableCollision")] private void InputDisableCollision(EntityInputData data) => IsSolid = false;

    [EntityInput("SetBodyGroup")] private void InputSetBodyGroup(EntityInputData data) => SetBodyGroup(data.Parameter);

    [EntityInput("Skin")]
    private void InputSkin(EntityInputData data)
    {
        if (ModelNode is not { } node || NonEmpty(data.Parameter?.Trim()) is not { } value)
        {
            return;
        }

        var skins = node.GetMaterialGroups().ToList();
        string? skin;

        // A number is the skin's position, as the input is typed, even where a group is named like one:
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            // Every model has a skin 0, whether or not it names any groups
            if (index == 0 && skins.Count == 0)
            {
                return;
            }

            skin = index >= 0 && index < skins.Count ? skins[index] : null;
        }
        else
        {
            skin = skins.Find(name => name.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        if (skin == null)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' has no skin \"{Skin}\" on {Model}, which has [{Skins}]",
                Classname, TargetName, value, ModelName, string.Join(", ", skins));
            return;
        }

        node.SetMaterialGroup(skin);
    }

    // "<bodygroup>,<choice>", the choice by index or by the name modern models give it.
    private void SetBodyGroup(string? value)
    {
        if (ModelNode is not { } node || NonEmpty(value?.Trim()) is not { } parameter)
        {
            return;
        }

        var choices = node.GetMeshGroups().Select(ParseBodyGroupChoice).OfType<BodyGroupChoice>().ToList();

        var comma = parameter.IndexOf(',', StringComparison.Ordinal);
        var bodyGroup = comma < 0 ? null : parameter[..comma].Trim();
        var choiceText = comma < 0 ? parameter : parameter[(comma + 1)..].Trim();

        if (comma < 0)
        {
            var bodyGroups = choices.Select(static choice => choice.BodyGroup).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bodyGroup = bodyGroups.Count == 1 ? bodyGroups[0] : null;
        }

        var isIndex = int.TryParse(choiceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index);

        var match = bodyGroup == null ? null : choices.Find(choice =>
            choice.BodyGroup.Equals(bodyGroup, StringComparison.OrdinalIgnoreCase)
            && (isIndex ? choice.Index == index : string.Equals(choice.Name, choiceText, StringComparison.OrdinalIgnoreCase)));

        if (match == null)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' has no body group choice \"{Value}\" on {Model}, which has [{Choices}]",
                Classname, TargetName, parameter, ModelName, string.Join(", ", choices.Select(static choice => choice.MeshGroup)));
            return;
        }

        var active = node.GetActiveMeshGroups()
            .Where(meshGroup => ParseBodyGroupChoice(meshGroup) is not { } choice
                || !choice.BodyGroup.Equals(match.BodyGroup, StringComparison.OrdinalIgnoreCase))
            .Append(match.MeshGroup)
            .ToList();

        node.SetActiveMeshGroups(active);
    }

    private sealed record BodyGroupChoice(string MeshGroup, string BodyGroup, int Index, string? Name);

    private static BodyGroupChoice? ParseBodyGroupChoice(string meshGroup)
    {
        // HL:A "<bodygroup>_@<index>"
        // CS2  "<bodygroup>_@<index>_#&<choice name>"

        var joiner = meshGroup.IndexOf("_@", StringComparison.Ordinal);

        if (joiner <= 0)
        {
            return null;
        }

        var rest = meshGroup[(joiner + 2)..];
        var nameJoiner = rest.IndexOf("_#&", StringComparison.Ordinal);
        var indexText = nameJoiner < 0 ? rest : rest[..nameJoiner];

        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return null;
        }

        return new BodyGroupChoice(meshGroup, meshGroup[..joiner], index, nameJoiner < 0 ? null : rest[(nameJoiner + 3)..]);
    }

    private void PlayForcedAnimation(EntityInputData data, bool? looping, bool restart)
    {
        if (NonEmpty(data.Parameter) is { } name)
        {
            PlayAnimation(name, looping, restart, forced: true);
        }
    }

    private void SetIdleAnimation(EntityInputData data, bool? looping)
    {
        IdleAnimation = NonEmpty(data.Parameter);
        idleLooping = looping;

        // An animation still playing hands over when it ends; one that already has is replaced now
        if (!isWaitingForAnimationEnd && ModelNode is { } node && HasAnimationEnded(node.AnimationController))
        {
            ReturnToIdle();
        }
    }

    private void ReturnToIdle()
    {
        if (HoldAnimation || IdleAnimation == null || ModelNode is not { } node)
        {
            return;
        }

        // An idle that has run out itself stays on its last frame rather than restarting forever
        if (string.Equals(node.AnimationController.ActiveAnimation?.Name, IdleAnimation, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PlayAnimation(IdleAnimation, idleLooping, restart: true, forced: false);
    }

    /// <param name="name">The animation to play.</param>
    /// <param name="looping">Whether it loops; null takes a sequence's own flag, and loops anything else.</param>
    /// <param name="restart">Whether an animation already playing starts over, rather than being left alone.</param>
    /// <param name="forced">Whether an input asked for it, which is what the animation outputs report on.</param>
    private void PlayAnimation(string name, bool? looping, bool restart, bool forced)
    {
        if (ModelNode is not { HasMeshes: true } node)
        {
            return;
        }

        if (!node.Animations.TryGetValue(name, out var animation))
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' has no animation \"{Animation}\"", Classname, TargetName, name);
            return;
        }

        var controller = node.AnimationController;

        if (!restart && controller.ActiveAnimation == animation)
        {
            return;
        }

        var loops = looping ?? (animation is not SequenceAnimation sequence || sequence.IsLooping);

        // An input's animation fades in over the sequence's own fade-in, as a sequence change does
        var blendTime = 0f;

        if (forced && controller.ActiveAnimation != null && animation is SequenceAnimation fadingSequence)
        {
            blendTime = fadingSequence.SequenceParams.FadeInTime;
        }

        controller.Looping = loops;
        node.SetAnimation(animation, blendTime);

        // A non-looping animation that ran out paused the whole player, which a new one does not undo
        controller.IsPaused = false;

        isForcedAnimation = forced;
        isWaitingForAnimationEnd = !loops;

        if (isWaitingForAnimationEnd)
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }

        if (forced)
        {
            EntitySystem.TriggerOutput(this, "OnAnimationBegun");
        }
    }

    // A single-frame animation never advances, so it has ended as soon as it has started
    private static bool HasAnimationEnded(AnimationController controller)
        => controller.ActiveClipFinished || (controller.ActiveAnimation is { FrameCount: <= 1 } && !controller.Looping);

    // Absent, or CS2's "use sequence settings", leaves it to the animation
    private bool? ReadLoopMode(string key) => KeyValues.GetStringProperty(key) switch
    {
        "ANIM_LOOP_MODE_LOOPING" => true,
        "ANIM_LOOP_MODE_NOT_LOOPING" => false,
        _ => null,
    };

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
