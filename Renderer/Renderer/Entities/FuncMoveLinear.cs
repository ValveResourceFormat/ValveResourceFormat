using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_movelinear</c>. A brush that moves <c>movedistance</c> along <c>movedir</c> on command, to either
/// end or any fraction between. Damage to whatever blocks it is not simulated.
/// </summary>
public class FuncMoveLinear : BaseToggle
{
    /// <summary>What a <c>func_movelinear</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Things pass straight through it.</summary>
        NotSolid = 8,
    }

    /// <summary>Which point of the travel the map placed the brush at, the <c>authoredposition</c> keyvalue.</summary>
    public enum AuthoredPosition
    {
        /// <summary>At <c>startposition</c> of the way along, where it also spawns.</summary>
        StartPosition = 0,

        /// <summary>At the open end.</summary>
        OpenPosition = 1,

        /// <summary>At the closed end.</summary>
        ClosedPosition = 2,
    }

    /// <summary>Gets the closed end of the travel.</summary>
    public Vector3 PositionClosed { get; private set; }

    /// <summary>Gets the open end of the travel.</summary>
    public Vector3 PositionOpen { get; private set; }

    /// <summary>Gets how far apart the two ends are.</summary>
    public float MoveDistance { get; private set; }

    private string? soundStart;
    private string? soundStop;
    private string? currentSound;
    private SoundHandle startSound;
    private SoundHandle stopSound;

    /// <summary>Initializes a <c>func_movelinear</c> from its keyvalues.</summary>
    public FuncMoveLinear(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var localDirection = ResolveEntitySpaceMoveDirection();

        Lip = KeyValues.GetFloatProperty("lip");
        MoveDistance = KeyValues.GetFloatProperty("movedistance");

        // Without a distance it travels its own length, like a door
        if (MoveDistance <= 0f)
        {
            MoveDistance = GetTravelDistance(localDirection);
        }

        Speed = KeyValues.GetFloatProperty("speed");

        if (Speed <= 0f)
        {
            Speed = 100f;
        }

        var startPosition = KeyValues.GetFloatProperty("startposition");
        var authored = (AuthoredPosition)KeyValues.GetInt32Property("authoredposition");
        var travel = MoveDirection * MoveDistance;

        (PositionClosed, PositionOpen) = authored switch
        {
            AuthoredPosition.OpenPosition => (Origin - travel, Origin),
            AuthoredPosition.ClosedPosition => (Origin, Origin + travel),
            _ => (Origin - travel * startPosition, Origin - travel * startPosition + travel),
        };

        // Authored at an end but starting elsewhere, it spawns where it starts
        if ((authored == AuthoredPosition.OpenPosition && startPosition != 1f)
            || (authored == AuthoredPosition.ClosedPosition && startPosition != 0f))
        {
            JumpTo(PositionClosed + travel * startPosition, Angles);
        }

        if (HasSpawnFlags(SpawnFlag.NotSolid))
        {
            IsSolid = false;
        }

        MovesWithoutPushing = KeyValues.GetBooleanProperty("movewithoutpushingblockers");

        soundStart = NonEmpty(KeyValues.GetStringProperty("startsound"));
        soundStop = NonEmpty(KeyValues.GetStringProperty("stopsound"));

        foreach (var sound in (string?[])[soundStart, soundStop])
        {
            if (sound != null)
            {
                Sound.Cache(sound);
            }
        }
    }

    /// <summary>
    /// Fires <c>OnFullyOpen</c> or <c>OnFullyClosed</c> when arriving at an end. The move sound stops 0.1s
    /// later, since another move may follow straight on.
    /// </summary>
    public override void MoveDone()
    {
        FinishLinearMove();

        SetNextThink(EntitySystem.CurrentTime + 0.1f);

        if (Vector3.Distance(Origin, PositionOpen) < 0.001f)
        {
            EntitySystem.TriggerOutput(this, "OnFullyOpen", this);
        }
        else if (Vector3.Distance(Origin, PositionClosed) < 0.001f)
        {
            EntitySystem.TriggerOutput(this, "OnFullyClosed", this);
        }
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        startSound.Stop();
        stopSound.Stop();

        base.OnRemove();
    }

    /// <summary>Stops the start sound and plays the stop sound, once the brush has come to rest.</summary>
    public override void Think()
    {
        if (soundStart != null && currentSound == soundStart)
        {
            startSound.Stop();
        }

        if (soundStop != null && currentSound != soundStop)
        {
            currentSound = soundStop;
            stopSound = Sound.Play(soundStop, WorldOrigin);
        }
    }

    // Protected rather than private, so a subclass's input table inherits them

    /// <summary>Travels to the open end.</summary>
    [EntityInput("Open")]
    protected void InputOpen(EntityInputData data)
    {
        if (Origin != PositionOpen)
        {
            MoveTo(PositionOpen);
        }
    }

    /// <summary>Travels to the closed end.</summary>
    [EntityInput("Close")]
    protected void InputClose(EntityInputData data)
    {
        if (Origin != PositionClosed)
        {
            MoveTo(PositionClosed);
        }
    }

    /// <summary>Travels to a fraction of the way from the closed end to the open end.</summary>
    [EntityInput("SetPosition")]
    protected void InputSetPosition(EntityInputData data)
    {
        var target = Vector3.Lerp(PositionClosed, PositionOpen, data.Float());

        if (Vector3.Distance(target, Origin) > 0.001f)
        {
            MoveTo(target);
        }
    }

    /// <summary>Changes the speed, keeping the destination. Zero stops the brush where it is.</summary>
    [EntityInput("SetSpeed")]
    protected void InputSetSpeed(EntityInputData data)
    {
        Speed = data.Float();

        if (!IsLinearMoving)
        {
            return;
        }

        if (MathF.Abs(Speed) > float.Epsilon)
        {
            LinearMove(FinalDestination);
            return;
        }

        Speed = 1f;
        LinearMove(Origin);
    }

    /// <summary>
    /// Jumps to the named entity's origin, which becomes the closed end. A travel under way is not stopped:
    /// it lands back here when its time is up.
    /// </summary>
    [EntityInput("TeleportToTarget")]
    protected void InputTeleportToTarget(EntityInputData data)
    {
        var target = string.IsNullOrEmpty(data.Parameter)
            ? null
            : EntitySystem.FindTargets(new EntityIOTarget(data.Parameter, EntityIOTargetType.EntityName), data.Activator, data.Caller).FirstOrDefault();

        if (target == null)
        {
            EntitySystem.Logger.LogWarning("func_movelinear '{TargetName}' teleport target '{Target}' was not found", TargetName, data.Parameter);
            return;
        }

        Teleport(target.RigidTransform.Translation, null);

        SetEndsFromStart(0f);
    }

    /// <summary>
    /// Makes the current position the given fraction along the travel, moving both ends. Like a teleport,
    /// a travel under way lands back here.
    /// </summary>
    [EntityInput("ResetPosition")]
    protected void InputResetPosition(EntityInputData data) => SetEndsFromStart(data.Float());

    /// <summary>Changes the travel, keeping the closed end where it is.</summary>
    [EntityInput("SetMoveDistanceFromStart")]
    protected void InputSetMoveDistanceFromStart(EntityInputData data)
    {
        MoveDistance = data.Float();
        PositionOpen = PositionClosed + MoveDirection * MoveDistance;
    }

    /// <summary>Changes the travel, keeping the open end where it is.</summary>
    [EntityInput("SetMoveDistanceFromEnd")]
    protected void InputSetMoveDistanceFromEnd(EntityInputData data)
    {
        MoveDistance = data.Float();
        PositionClosed = PositionOpen - MoveDirection * MoveDistance;
    }

    private void SetEndsFromStart(float startPosition)
    {
        PositionClosed = Origin - MoveDirection * MoveDistance * startPosition;
        PositionOpen = PositionClosed + MoveDirection * MoveDistance;
        FinalDestination = Origin;
    }

    private void MoveTo(Vector3 position)
    {
        if (Speed == 0f)
        {
            return;
        }

        if (soundStart != null)
        {
            if (currentSound == soundStart)
            {
                stopSound.Stop();
            }
            else
            {
                currentSound = soundStart;
                startSound = Sound.Play(soundStart, WorldOrigin);
            }
        }

        LinearMove(position);

        // Setting off again cancels the stop sound an arrival just scheduled
        SetNextThink(-1f);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
