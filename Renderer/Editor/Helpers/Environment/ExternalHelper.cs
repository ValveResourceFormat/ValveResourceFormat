using System.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// What the game draws of an entity the editor shows through an object of the game, which the declaration
/// names: the sprite of an <c>env_sprite</c>, or the panel of a <c>point_clientui_world_text_panel</c>.
/// </summary>
public sealed class ExternalHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <summary>Gets the name of the object of the game the entity is shown through, such as <c>ToolObjects/env_sprite</c>.</summary>
    public string ToolObject => GetArgument(0) ?? string.Empty;

    /// <summary>Initializes what the game draws of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public ExternalHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (ToolObject.Equals("ToolObjects/env_sprite", StringComparison.OrdinalIgnoreCase))
        {
            AddSprite();
        }
        else if (ToolObject.Equals("ToolObjects/point_clientui_world_text_panel", StringComparison.OrdinalIgnoreCase))
        {
            var panel = PointClientUIWorldPanel.CreatePanel(Owner.Scene, Owner.KeyValues, Owner.Classname);

            panel.Transform = Owner.RigidTransform;
            Owner.AddNode(panel, followsEntity: true);
        }
    }

    private void AddSprite()
    {
        if (GetStringKey("model") is not { Length: > 0 } materialName
            || !Path.GetExtension(materialName).Equals(".vmat", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var scene = Owner.Scene;
        var resource = scene.RendererContext.FileLoader.LoadFileCompiled(materialName);

        if (resource == null)
        {
            return;
        }

        // Tinted by the render color, scaled up for HDR, and as see-through as the render amount says
        var color = Owner.KeyValues.GetColor32Property("rendercolor") * GetFloatKey("hdrcolorscale", 1f);

        var sprite = new SpriteSceneNode(scene, scene.RendererContext, resource, Owner.Transform.Translation)
        {
            Name = materialName,
            Flags = ObjectTypeFlags.NoShadows,
            TintAlpha = new Vector4(color, GetFloatKey("renderamt", 255f) / 255f),
        };

        Owner.AddNode(sprite, followsEntity: true);
    }
}
