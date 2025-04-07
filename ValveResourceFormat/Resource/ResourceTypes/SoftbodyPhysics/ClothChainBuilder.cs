using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.SoftbodyPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.ModelExtract;

namespace ValveResourceFormat.Resource.ResourceTypes.SoftbodyPhysics;
public class ClothChainBuilder
{
    public ClothChainBuilder()
    {
        globalAttributes = [];
        joints = [];
    }

    public void SetAttribute(string property, float value)
    {
        globalAttributes[property] = value;
    }

    public void AddJoint(FeModelAggregateData.FeModelNode node)
    {
        joints.Add(node);
    }

    public KVObject Build()
    {
        var node = new KVObject(null, 3);
        node.AddProperty("_class", "ClothChain");
        var rootBone = (joints.Count > 0) ? joints[0].ControlBone : "";
        node.AddProperty("root_bone", rootBone);

        var chain = new KVObject(null, 2);
        node.AddProperty("chain", chain);

        var jointsList = new KVObject(null, true, joints.Count);
        chain.AddProperty("joints", jointsList);

        foreach (var joint in joints)
        {
            jointsList.AddItem(joint.MakeClothChainJoint());
        }

        var attributeList = new KVObject(null, true, defaultAttributeNodes.Length);
        chain.AddProperty("attr", attributeList);

        foreach (var attribute in defaultAttributeNodes)
        {
            var attributeNode = new KVObject(attribute.Name, attribute.Params);
            if (globalAttributes.TryGetValue(attribute.Name, out var value))
            {
                attributeNode.AddProperty("default", value);
            }
            attributeList.AddProperty(attribute.Name, attributeNode);
        }

        node.AddProperty("selection", new KVObject("selection", true));
        node.AddProperty("version", 1);

        return node;
    }

    private readonly Dictionary<string, float> globalAttributes;

    private readonly List<FeModelAggregateData.FeModelNode> joints;

    private readonly static (string Name, (string, object)[] Params)[] defaultAttributeNodes = [
        ("joint_name", [
            ("display", "Joint Name"),
            ("show", true),
            ("ui_order", 1),
            ("default", ""),
            ("lock", true)]),
        ("joint_parent",
 [            ("display", "Parent Joint"),
            ("show", false),
            ("ui_order", 2),
            ("default", "")]),
        ("simulate",
 [            ("display", "Simulate"),
            ("show", true),
            ("ui_order", 3),
            ("default", true)]),
        ("allow_rotation",
 [            ("display", "Allow Rotation"),
            ("show", true),
            ("ui_order", 4),
            ("default", true)]),
        ("stretch_spring",
 [            ("display", "Stretch Spring"),
            ("show", false),
            ("ui_order", 5),
            ("default", 1.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("child_sibling_spring",
 [            ("display", "Spring Between Children"),
            ("show", false),
            ("ui_order", 6),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("bend_spring",
 [            ("display", "Bend Spring"),
            ("show", false),
            ("ui_order", 7),
            ("default", 1.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("torsion_spring",
 [            ("display", "Torsion Spring"),
            ("show", false),
            ("ui_order", 8),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("explicit_length",
 [            ("display", "Explicit Length"),
            ("show", false),
            ("ui_order", 9),
            ("default", 0.0),
            ("min", 0.0)]),
        ("world_collision",
 [            ("display", "World Collision"),
            ("show", false),
            ("ui_order", 10),
            ("default", false)]),
        ("animated_length",
 [            ("display", "Animated Length"),
            ("show", false),
            ("ui_order", 11),
            ("default", false)]),
        ("goal_strength",
 [            ("display", "Goal Strength"),
            ("show", true),
            ("ui_order", 12),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("goal_damping",
 [            ("display", "Goal Damping"),
            ("show", false),
            ("ui_order", 13),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("drag",
 [            ("display", "Extra Drag"),
            ("show", false),
            ("ui_order", 14),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("mass",
 [            ("display", "Mass"),
            ("show", false),
            ("ui_order", 15),
            ("default", 1.0),
            ("min", 0.0)]),
        ("gravity_z",
 [            ("display", "Gravity"),
            ("show", true),
            ("ui_order", 16),
            ("default", 1.0)]),
        ("collision_radius",
 [            ("display", "Collision Radius"),
            ("show", true),
            ("ui_order", 17),
            ("default", 0.0),
            ("min", 0.0)]),
        ("lock_translation",
 [            ("display", "Lock Translation"),
            ("show", false),
            ("ui_order", 18),
            ("default", false)]),
        ("suspender",
 [            ("display", "Suspender Spring"),
            ("show", false),
            ("ui_order", 19),
            ("default", 0.0)]),
        ("antishrink",
 [            ("display", "Antishrink Strength"),
            ("show", false),
            ("ui_order", 20),
            ("default", 1.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("stray_radius",
 [            ("display", "Stray Radius"),
            ("show", true),
            ("ui_order", 21),
            ("default", 0.0),
            ("min", 0.0)]),
        ("stray_radius_stretchiness",
 [            ("display", "Stray Radius Stretchiness"),
            ("show", true),
            ("ui_order", 22),
            ("default", 0.0),
            ("min", 0.0)]),
        ("friction",
 [            ("display", "Friction"),
            ("show", true),
            ("ui_order", 23),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("vertex_map",
 [            ("display", "Vertex Map"),
            ("show", false),
            ("ui_order", 24),
            ("default", ""),
            ("verify", "vertex_map")]),
        ("end_effector",
 [            ("display", "End Effector"),
            ("show", false),
            ("ui_order", 25),
            ("default", 0.0),
            ("lock_default_value", true)]),
        ("stiff_hinge",
 [            ("display", "Stiff Hinge"),
            ("show", false),
            ("ui_order", 26),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0),
            ("lock_root2", true)]),
        ("stiff_hinge_angle",
 [            ("display", "Stiff Hinge Angle"),
            ("show", true),
            ("ui_order", 27),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 180.0),
            ("lock_root2", true)]),
        ("motion_bias",
 [            ("display", "Motion Bias"),
            ("show", false),
            ("ui_order", 28),
            ("default", 0.0),
            ("min", -1.0),
            ("max", 1.0),
            ("lock_root", true)]),
        ("extra_iterations",
 [            ("display", "Extra Iterations"),
            ("show", false),
            ("ui_order", 29),
            ("default", 0),
            ("min", 0),
            ("max", 1000)]),
        ("twist_relax",
 [            ("display", "Twist Relax"),
            ("show", false),
            ("ui_order", 30),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)]),
        ("extrude_sides",
 [            ("display", "Extrude Sides"),
            ("show", false),
            ("ui_order", 31),
            ("default", 0),
            ("min", 0),
            ("max", 4)]),
        ("extrude_radius",
 [            ("display", "Extrude Radius"),
            ("show", false),
            ("ui_order", 32),
            ("default", 5.0),
            ("min", 0.0)]),
        ("extrude_twist",
 [            ("display", "Extrude Twist"),
            ("show", false),
            ("ui_order", 33),
            ("default", 0.0)]),
        ("extrude_forward_axis",
 [            ("display", "Extrude Forward Axis"),
            ("show", false),
            ("ui_order", 34),
            ("default", ""),
            ("verify", "extrude_forward_axis")])
        ];
}
