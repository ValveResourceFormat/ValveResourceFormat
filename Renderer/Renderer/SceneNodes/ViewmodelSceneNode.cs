
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// Animgraph 2 model node.
/// </summary>
public class ViewmodelSceneNode : ModelSceneNode
{
    /// <summary>
    /// Toggle rendering.
    /// </summary>
    public bool Visible { get; set; }

    /// <summary>
    /// Viewmodel offset in viewmodel space (forward, right, up).
    /// </summary>
    public Vector3 ViewmodelOffset { get; set; } = new Vector3(-6, -2, -2);

    /// <summary>
    /// The player arms.
    /// </summary>
    public ModelSceneNode Arms => this;

    // public readonly List<ModelSceneNode?> Items = [];

    private int PreviousSelectedItem;

    public int SelectedItem
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            PreviousSelectedItem = field;
            field = value;
            OnSelectedItemChanged();
        }
    }

    private int CurrentAnim { get; set; }
    private int lastAnimFrame = -1;
    private string? lastAnimName;

    private void OnSelectedItemChanged()
    {
        if (ItemAnimations.TryGetValue(SelectedItem, out var anim))
        {
            currentIdleAnim = anim.Idle;
            currentDrawAnim = anim.Draw;
            PlayAnimation(currentDrawAnim);
        }
    }

    private string? currentIdleAnim;
    private string? currentDrawAnim;

    private void PlayIdleAnimation()
    {
        if (currentIdleAnim != null)
        {
            SetAnimationByName(currentIdleAnim);
        }
    }

    private void PlayAnimation(string? animationName)
    {
        if (animationName != null)
        {
            SetAnimationByName(animationName);
        }
    }

    internal ViewmodelSceneNode(Scene scene, Model model)
        : base(scene, model, null, true)
    {
    }

    record struct Anim(string Idle, string Draw, string LookAt);

    Dictionary<int, Anim> ItemAnimations = new()
    {
        [1] = new Anim(
            "rifle/rifle_m4a4/idle1_m4a4.vnmclip",
            "rifle/rifle_m4a4/draw_m4a4.vnmclip",
            "rifle/rifle_m4a4/lookat01_m4a4.vnmclip"
        ),
        [2] = new Anim(
            "pistol/_default_pistol/idle_pistol.vnmclip",
            "pistol/_default_pistol/draw_karambit.vnmclip",
            "pistol/_default_pistol/lookat01_pistol.vnmclip"
        ),
        [3] = new Anim(
            "knife/knife_karambit/idle1_karambit.vnmclip",
            "knife/knife_karambit/draw_karambit.vnmclip",
            "knife/knife_karambit/lookat01_karambit.vnmclip"
        ),
    };

    private void AddItem(Model item)
    {
        var model = new ModelSceneNode(Scene, item);
        // Items.Add(model);
    }

    /// <summary>
    /// Try to load the CS2 viewmodel, returning null when the required resources are not found.
    /// </summary>
    /// <param name="scene"></param>
    /// <returns></returns>
    public static ViewmodelSceneNode? TryLoadCs2Viewmodel(Scene scene)
    {
        var loader = scene.RendererContext.FileLoader;

        Span<string> resources = [
            "phase2/characters/models/ctm_st6/ctm_st6_varianti_ag2.vmdl",
            "weapons/models/m4a4/weapon_rif_m4a4.vmdl",
            "weapons/models/usp_silencer/weapon_pist_usp_silencer.vmdl",
            "weapons/models/knife/knife_karambit/weapon_knife_karambit.vmdl",
        ];

        List<Model> models = [];
        foreach (var name in resources)
        {
            var resource = loader.LoadFileCompiled(name);
            if (resource?.DataBlock is not Model model)
            {
                return null;
            }

            models.Add(model);
        }

        var viewmodel = new ViewmodelSceneNode(scene, models[0]);
        foreach (var item in models[1..])
        {
            // viewmodel.AddItem(item);
        }

        viewmodel.Arms.SetActiveMeshGroups([
            "first_or_third_person_@2_#&firstperson_default"
        ]);

        viewmodel.SetAnimationByName("animation/anims/viewmodel/knife/knife_karambit/idle1_karambit.vnmclip");
        viewmodel.LayerName = "world_layer_base";
        viewmodel.Flags |= ObjectTypeFlags.DisableVisCulling;

        scene.RendererContext.Logger.LogInformation($"Loaded viewmodel");

        scene.Add(viewmodel, true);
        return viewmodel;
    }

    /// <summary>
    /// Process input for the viewmodel, updating its transform to match the camera's orientation and position.
    /// </summary>
    /// <param name="input"></param>
    public void ProcessInput(UserInput input)
    {
        Visible = true;

        var camera = input.Camera;
        camera.RecalculateDirectionVectors();

        // Build a stable camera orientation quaternion from direction vectors.
        var forward = Vector3.Normalize(camera.Forward);
        var worldUp = Vector3.UnitZ;

        var right = Vector3.Normalize(Vector3.Cross(worldUp, forward));
        if (right.LengthSquared() < 1e-4f)
        {
            // Looking straight up/down: fallback to camera's right vector.
            right = Vector3.Normalize(camera.Right);
        }

        var up = Vector3.Cross(forward, right);

        var cameraRotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            0, 0, 0, 1
        ));

        // Apply a fixed viewmodel-space rotation to match the expected model orientation.
        var viewmodelOffsetRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -float.DegreesToRadians(90))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -float.DegreesToRadians(90));
        var viewmodelRotation = Quaternion.Normalize(cameraRotation * viewmodelOffsetRot);

        var rotationMatrix = Matrix4x4.CreateFromQuaternion(viewmodelRotation);
        var offset = Vector3.Transform(ViewmodelOffset, viewmodelRotation);

        Transform = rotationMatrix with { Translation = camera.Location + offset };
    }

    /// <summary>
    /// Update
    /// </summary>
    public override void Update(Scene.UpdateContext context)
    {
        base.Update(context);

        var active = AnimationController.ActiveAnimation;
        if (active != null)
        {
            var frame = AnimationController.Frame;

            if (lastAnimName != active.Name)
            {
                lastAnimName = active.Name;
                lastAnimFrame = frame;
            }
            else if (!active.IsLooping && frame < lastAnimFrame)
            {
                // Animation just ended (non-looping). Fall back to idle.
                PlayIdleAnimation();

                lastAnimName = currentIdleAnim;
                lastAnimFrame = 0;
            }

            lastAnimFrame = frame;
        }
        else
        {
            lastAnimName = null;
            lastAnimFrame = -1;
        }

        // Arms should always be visible if the viewmodel is visible
        LocalBoundingBox = new AABB(Vector3.Zero, float.PositiveInfinity);
    }
}