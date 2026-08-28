using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// Third-person player character for Deadlock walk mode. Follows the movement controller
/// and drives the hero's loose locomotion clips (8-way runs, slides, dashes, mantles,
/// jumps) from the controller's state.
/// </summary>
public class DeadlockPlayerSceneNode : ModelSceneNode
{
    internal const string ThirdPersonLayerName = "Internal - Third Person Player";

    private readonly string clipPrefix;
    private string? currentClip;

    private static readonly string[] HeadingSuffixes = ["n", "ne", "e", "se", "s", "sw", "w", "nw"];

    private DeadlockPlayerSceneNode(Scene scene, Model model, string clipPrefix)
        : base(scene, model, isWorldPreview: true)
    {
        this.clipPrefix = clipPrefix;

        LayerName = ThirdPersonLayerName;
        Flags |= ObjectTypeFlags.DisableVisCulling;

        LoadClips();
    }

    /// <summary>
    /// Loads the hero model and its locomotion clips, returning null when the model is not
    /// available in the loaded packages.
    /// </summary>
    public static DeadlockPlayerSceneNode? TryLoad(Scene scene, string modelPath)
    {
        var resource = scene.RendererContext.FileLoader.LoadFileCompiled(modelPath);

        if (resource?.DataBlock is not Model model)
        {
            return null;
        }

        var clipPrefix = modelPath[..(modelPath.LastIndexOf('/') + 1)] + "clips/";

        var node = new DeadlockPlayerSceneNode(scene, model, clipPrefix);
        scene.Add(node, true);
        scene.DeactivateLayer(ThirdPersonLayerName);

        scene.RendererContext.Logger.LogInformation("Loaded third person player model {Model}.", modelPath);

        return node;
    }

    private void LoadClips()
    {
        List<string> clips = [
            "out_of_combat_stand_idle",
            "out_of_combat_crouch_idle",
            "jump_ground",
            "jump_air",
            "jump_dash",
            "in_air_apex",
            "in_air_loop_down",
            "landing_impact_idle",
            "slide_start",
            "slide_loop",
            "slide_getup",
            "dash_ground",
            "mantle_32",
            "mantle_64",
            "mantle_96",
            "mantle_128",
        ];

        foreach (var heading in HeadingSuffixes)
        {
            clips.Add("out_of_combat_run_" + heading);
            clips.Add("out_of_combat_crouch_run_" + heading);
        }

        foreach (var clip in clips)
        {
            if (!LoadAnimationClip(clipPrefix + clip + ".vnmclip"))
            {
                Scene.RendererContext.Logger.LogWarning("Missing player clip: {Clip}", clipPrefix + clip);
            }
        }
    }

    /// <summary>
    /// Follows the movement controller: places the model at the player's feet facing the
    /// camera yaw, keeps the layer in sync with walk mode, and picks the locomotion clip.
    /// </summary>
    public void ProcessInput(UserInput input)
    {
        if (input.NoClip)
        {
            if (LayerEnabled)
            {
                Scene.DeactivateLayer(ThirdPersonLayerName);
            }

            return;
        }

        if (!LayerEnabled)
        {
            Scene.ActivateLayer(ThirdPersonLayerName);
        }

        var movement = input.PlayerMovement;
        var yaw = input.Camera.Yaw;

        Transform = Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, yaw)
            * Matrix4x4.CreateTranslation(movement.Position);

        var (clip, looping) = PickClip(movement, yaw);
        SetClip(clip, looping);
    }

    private (string Clip, bool Looping) PickClip(PlayerMovement movement, float yaw)
    {
        if (movement.IsMantling)
        {
            var height = movement.MantleHeight;
            var mantleClip = height <= 48f ? "mantle_32"
                : height <= 80f ? "mantle_64"
                : height <= 112f ? "mantle_96"
                : "mantle_128";
            return (mantleClip, false);
        }

        if (movement.IsDashing)
        {
            return ("dash_ground", false);
        }

        if (movement.IsSliding)
        {
            return ("slide_loop", true);
        }

        if (!movement.OnGround)
        {
            if (movement.Velocity.Z < -60f)
            {
                return ("in_air_loop_down", true);
            }

            if (movement.Velocity.Z > 60f)
            {
                // Whichever launch put the player on this arc
                if (movement.DashJumped)
                {
                    return ("jump_dash", false);
                }

                return (currentClip == "jump_air" || (movement.Jumped && !movement.WasOnGroundLastFrame)
                    ? "jump_air"
                    : "jump_ground", false);
            }

            return ("in_air_apex", true);
        }

        var crouched = movement.CrouchBlend > 0.5f;
        var horizontalVelocity = new Vector2(movement.Velocity.X, movement.Velocity.Y);
        var speed = horizontalVelocity.Length();

        if (speed < 20f)
        {
            return (crouched ? "out_of_combat_crouch_idle" : "out_of_combat_stand_idle", true);
        }

        // 8-way heading of the velocity relative to the facing: n is straight ahead,
        // e is strafing right
        var (sinYaw, cosYaw) = MathF.SinCos(yaw);
        var forward = new Vector2(cosYaw, sinYaw);
        var right = new Vector2(sinYaw, -cosYaw);
        var direction = horizontalVelocity / speed;

        var angle = MathF.Atan2(Vector2.Dot(direction, right), Vector2.Dot(direction, forward));
        var octant = (int)MathF.Round(angle / (MathF.PI / 4f)) & 7;
        var suffix = HeadingSuffixes[octant];

        return ((crouched ? "out_of_combat_crouch_run_" : "out_of_combat_run_") + suffix, true);
    }

    private void SetClip(string clip, bool looping)
    {
        if (clip == currentClip)
        {
            return;
        }

        currentClip = clip;
        AnimationController.IsPaused = false;
        AnimationController.Looping = looping;
        SetAnimationByName(clipPrefix + clip + ".vnmclip", blendTime: 0.15f);
    }
}
