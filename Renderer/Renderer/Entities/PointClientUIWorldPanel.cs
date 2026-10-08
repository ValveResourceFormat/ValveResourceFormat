using System;
using System.Collections.Generic;
using System.Text;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

class PointClientUIWorldPanel : BaseEntity
{
 
    public PointClientUIWorldPanel(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    protected override SceneNode? CreateRootNode()
    {
        var width = KeyValues.GetFloatProperty("width");
        var height = KeyValues.GetFloatProperty("height");

        var orientation = (WorldPanelSceneNode.Orientation)KeyValues.GetInt32Property("orientation", 0);

        return new WorldPanelSceneNode(Scene, width, height, orientation, RigidTransform, Color32.White);
    }
}
