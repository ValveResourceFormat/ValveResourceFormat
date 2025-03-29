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

        var jointsList = new KVObject(null, true, joints.Count);

        foreach (var joint in joints)
        {
            var clothJoint = new KVObject(null,
                ("joint_name", joint.ControlBone),
                ("simulate", !joint.IsStatic),
                ("allow_rotation", joint.AllowRotation),
                ("goal_strength", joint.GoalStrength),
                ("goal_damping", joint.GoalDamping),
                ("mass", joint.Mass),
                ("gravity_z", joint.Gravity),
                ("collision_radius", joint.CollissionRadius)
                );
            jointsList.AddItem(clothJoint);
        }

        return node;
    }

    private readonly Dictionary<string, float> globalAttributes;

    private readonly List<FeModelAggregateData.FeModelNode> joints;

    private readonly static KVObject[] defaultAttributeNodes = [
        new KVObject("joint_name",
            ("display", "Joint Name"),
            ("show", true),
            ("ui_order", 1),
            ("default", ""),
            ("lock", true)),
        new KVObject("joint_parent",
            ("display", "Parent Joint"),
            ("show", false),
            ("ui_order", 2),
            ("default", "")),
        new KVObject("simulate",
            ("display", "Simulate"),
            ("show", true),
            ("ui_order", 3),
            ("default", true)),
        new KVObject("allow_rotation",
            ("display", "Allow Rotation"),
            ("show", true),
            ("ui_order", 4),
            ("default", true)),
        new KVObject("stretch_spring",
            ("display", "Stretch Spring"),
            ("show", false),
            ("ui_order", 5),
            ("default", 1.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("child_sibling_spring",
            ("display", "Spring Between Children"),
            ("show", false),
            ("ui_order", 6),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("bend_spring",
            ("display", "Bend Spring"),
            ("show", false),
            ("ui_order", 7),
            ("default", 1.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("torsion_spring",
            ("display", "Torsion Spring"),
            ("show", false),
            ("ui_order", 8),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("explicit_length",
            ("display", "Explicit Length"),
            ("show", false),
            ("ui_order", 9),
            ("default", 0.0),
            ("min", 0.0)),
        new KVObject("world_collision",
            ("display", "World Collision"),
            ("show", false),
            ("ui_order", 10),
            ("default", false)),
        new KVObject("animated_length",
            ("display", "Animated Length"),
            ("show", false),
            ("ui_order", 11),
            ("default", false)),
        new KVObject("goal_strength",
            ("display", "Goal Strength"),
            ("show", true),
            ("ui_order", 12),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("goal_damping",
            ("display", "Goal Damping"),
            ("show", false),
            ("ui_order", 13),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("drag",
            ("display", "Extra Drag"),
            ("show", false),
            ("ui_order", 14),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("mass",
            ("display", "Mass"),
            ("show", false),
            ("ui_order", 15),
            ("default", 1.0),
            ("min", 0.0)),
        new KVObject("gravity_z",
            ("display", "Gravity"),
            ("show", true),
            ("ui_order", 16),
            ("default", 1.0)),
        new KVObject("collision_radius",
            ("display", "Collision Radius"),
            ("show", true),
            ("ui_order", 17),
            ("default", 0.0),
            ("min", 0.0)),
        new KVObject("lock_translation",
            ("display", "Lock Translation"),
            ("show", false),
            ("ui_order", 18),
            ("default", false)),
        new KVObject("suspender",
            ("display", "Suspender Spring"),
            ("show", false),
            ("ui_order", 19),
            ("default", 0.0)),
        new KVObject("antishrink",
            ("display", "Antishrink Strength"),
            ("show", false),
            ("ui_order", 20),
            ("default", 1.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("stray_radius",
            ("display", "Stray Radius"),
            ("show", true),
            ("ui_order", 21),
            ("default", 0.0),
            ("min", 0.0)),
        new KVObject("stray_radius_stretchiness",
            ("display", "Stray Radius Stretchiness"),
            ("show", true),
            ("ui_order", 22),
            ("default", 0.0),
            ("min", 0.0)),
        new KVObject("friction",
            ("display", "Friction"),
            ("show", true),
            ("ui_order", 23),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("vertex_map",
            ("display", "Vertex Map"),
            ("show", false),
            ("ui_order", 24),
            ("default", ""),
            ("verify", "vertex_map")),
        new KVObject("end_effector",
            ("display", "End Effector"),
            ("show", false),
            ("ui_order", 25),
            ("default", 0.0),
            ("lock_default_value", true)),
        new KVObject("stiff_hinge",
            ("display", "Stiff Hinge"),
            ("show", false),
            ("ui_order", 26),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0),
            ("lock_root2", true)),
        new KVObject("stiff_hinge_angle",
            ("display", "Stiff Hinge Angle"),
            ("show", true),
            ("ui_order", 27),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 180.0),
            ("lock_root2", true)),
        new KVObject("motion_bias",
            ("display", "Motion Bias"),
            ("show", false),
            ("ui_order", 28),
            ("default", 0.0),
            ("min", -1.0),
            ("max", 1.0),
            ("lock_root", true)),
        new KVObject("extra_iterations",
            ("display", "Extra Iterations"),
            ("show", false),
            ("ui_order", 29),
            ("default", 0),
            ("min", 0),
            ("max", 1000)),
        new KVObject("twist_relax",
            ("display", "Twist Relax"),
            ("show", false),
            ("ui_order", 30),
            ("default", 0.0),
            ("min", 0.0),
            ("max", 1.0)),
        new KVObject("extrude_sides",
            ("display", "Extrude Sides"),
            ("show", false),
            ("ui_order", 31),
            ("default", 0),
            ("min", 0),
            ("max", 4)),
        new KVObject("extrude_radius",
            ("display", "Extrude Radius"),
            ("show", false),
            ("ui_order", 32),
            ("default", 5.0),
            ("min", 0.0)),
        new KVObject("extrude_twist",
            ("display", "Extrude Twist"),
            ("show", false),
            ("ui_order", 33),
            ("default", 0.0)),
        new KVObject("extrude_forward_axis",
            ("display", "Extrude Forward Axis"),
            ("show", false),
            ("ui_order", 34),
            ("default", ""),
            ("verify", "extrude_forward_axis"))
        ];
}
