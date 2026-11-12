using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>texture_based_animatable</c>. A model of rigid pieces whose motion is baked into a position and a rotation
/// keyframe texture, one column per piece, which its <c>F_PRE_BAKED_VERTEX_ANIMATION</c> materials sample per vertex.
/// <c>Start</c> plays the frames at <c>fps</c> from the first, <c>Stop</c> holds the current one.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CTextureBasedAnimatable">CTextureBasedAnimatable</seealso>
public sealed class TextureBasedAnimatable : BaseModelEntity
{
    private const string FrameParam = "g_flFrame";

    private readonly List<RenderMaterial> animatedMaterials = [];
    private float fps;
    private bool loop;
    private float lastFrame;
    private float startTime;

    /// <summary>Initializes a texture based animatable from its keyvalues.</summary>
    public TextureBasedAnimatable(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        fps = KeyValues.GetFloatProperty("fps", 60f);
        loop = KeyValues.GetBooleanProperty("loop");
        IsDrawn = !KeyValues.GetBooleanProperty("startdisabled");

        if (ModelNode is not { } node
            || KeyValues.GetStringProperty("texture_based_animation_position_keys") is not { Length: > 0 } positionKeysName
            || KeyValues.GetStringProperty("texture_based_animation_rotation_keys") is not { Length: > 0 } rotationKeysName)
        {
            return;
        }

        var materialLoader = Scene.RendererContext.MaterialLoader;
        var positionKeys = materialLoader.GetTexture(positionKeysName);
        var rotationKeys = materialLoader.GetTexture(rotationKeysName);
        var invDimensions = new Vector4(1f / positionKeys.Width, 1f / positionKeys.Height, 0f, 0f);

        lastFrame = positionKeys.Height - 1;

        foreach (var mesh in node.AllRenderableMeshes)
        {
            foreach (var drawCall in mesh.DrawCalls)
            {
                if (drawCall.Material.IntParams.GetValueOrDefault("F_PRE_BAKED_VERTEX_ANIMATION") == 0)
                {
                    continue;
                }

                var material = materialLoader.LoadMaterial(drawCall.Material.Material.Resource, new Dictionary<string, byte>(drawCall.Material.Shader.Parameters));
                material.Textures["g_tPositionKeyFrames"] = positionKeys;
                material.Textures["g_tRotationKeyFrames"] = rotationKeys;
                material.VectorParams["g_vPositionKeyFramesInvDim"] = invDimensions;
                material.FloatParams[FrameParam] = 0f;

                drawCall.SetNewMaterial(material);
                animatedMaterials.Add(material);
            }
        }
    }

    /// <summary>Advances the frame while the animation plays.</summary>
    public override void Think()
    {
        var frame = (EntitySystem.CurrentTime - startTime) * fps;

        frame = loop ? frame % lastFrame : MathF.Min(frame, lastFrame);

        foreach (var material in animatedMaterials)
        {
            material.FloatParams[FrameParam] = frame;
        }

        if (loop || frame < lastFrame)
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }
    }

    /// <summary>Plays the animation from the first frame.</summary>
    [EntityInput("Start")]
    private void InputStart(EntityInputData data)
    {
        if (animatedMaterials.Count == 0)
        {
            return;
        }

        startTime = EntitySystem.CurrentTime;
        Think();
    }

    /// <summary>Holds the current frame.</summary>
    [EntityInput("Stop")]
    private void InputStop(EntityInputData data) => SetNextThink(-1f);

    /// <summary>Shows the model.</summary>
    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data) => IsDrawn = true;

    /// <summary>Hides the model.</summary>
    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data) => IsDrawn = false;
}
