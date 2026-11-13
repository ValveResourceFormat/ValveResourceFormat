using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>texture_based_animatable</c>. A model of rigid pieces whose motion is baked into a position keyframe texture and
/// a rotation keyframe texture, one column per piece. The <c>F_PRE_BAKED_VERTEX_ANIMATION</c> materials of the model sample them per vertex.
/// <c>Start</c> plays the frames at <c>fps</c> from the first, <c>Stop</c> returns to the first.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CTextureBasedAnimatable">CTextureBasedAnimatable</seealso>
public sealed partial class TextureBasedAnimatable : BaseModelEntity
{
    private const string FrameParam = "g_flFrame";

    private readonly List<RenderMaterial> animatedMaterials = [];
    private float fps;
    private bool loop;
    private float lastFrame;
    private float startTime;
    private AABB modelBounds;
    private AABB? animationBounds;

    /// <summary>Initializes a texture based animatable from its keyvalues.</summary>
    public TextureBasedAnimatable(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        fps = KeyValues.GetFloatProperty("fps");
        loop = KeyValues.GetBooleanProperty("loop");

        if (ModelNode is not { } node
            || KeyValues.GetStringProperty("texture_based_animation_position_keys") is not { Length: > 0 } positionKeysName
            || KeyValues.GetStringProperty("texture_based_animation_rotation_keys") is not { Length: > 0 } rotationKeysName)
        {
            return;
        }

        // The pieces move outside the model bounds, so while playing the model is culled by the animation bounds instead
        var boundsMin = KeyValues.GetVector3Property("anim_bounds_min");
        var boundsMax = KeyValues.GetVector3Property("anim_bounds_max");
        var hasAnimationBounds = boundsMin.X < boundsMax.X && boundsMin.Y < boundsMax.Y && boundsMin.Z < boundsMax.Z;

        modelBounds = node.LocalBoundingBox;
        animationBounds = hasAnimationBounds ? new AABB(boundsMin, boundsMax) : null;

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

        SetFrame(frame);

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

        if (animationBounds is { } bounds)
        {
            ModelNode?.LocalBoundingBox = bounds;
        }

        Think();
    }

    /// <summary>Stops the animation and returns to the first frame.</summary>
    [EntityInput("Stop")]
    private void InputStop(EntityInputData data)
    {
        if (animatedMaterials.Count == 0)
        {
            return;
        }

        SetNextThink(-1f);
        SetFrame(0f);

        ModelNode?.LocalBoundingBox = modelBounds;
    }

    /// <summary>Shows the model.</summary>
    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data) => IsDrawn = true;

    /// <summary>Hides the model.</summary>
    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data) => IsDrawn = false;

    private void SetFrame(float frame)
    {
        foreach (var material in animatedMaterials)
        {
            material.FloatParams[FrameParam] = frame;
        }
    }
}
